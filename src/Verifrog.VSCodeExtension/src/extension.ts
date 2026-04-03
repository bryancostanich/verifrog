import * as vscode from 'vscode';
import { SignalsProvider, SignalItem } from './signals';
import { CheckpointsProvider } from './checkpoints';
import { VerifrogDocumentSymbolProvider } from './symbolProvider';
import { VerifrogDebugAdapterFactory, VerifrogDebugConfigProvider } from './debugAdapter';

let signalsProvider: SignalsProvider;
let checkpointsProvider: CheckpointsProvider;

export function activate(context: vscode.ExtensionContext) {
    // Document symbols (outline) for .verifrog files
    context.subscriptions.push(
        vscode.languages.registerDocumentSymbolProvider(
            { language: 'verifrog' },
            new VerifrogDocumentSymbolProvider(),
        ),
    );

    // Initialize providers
    signalsProvider = new SignalsProvider();
    checkpointsProvider = new CheckpointsProvider();

    const signalsView = vscode.window.createTreeView('verifrog.signals', {
        treeDataProvider: signalsProvider,
        showCollapseAll: true,
    });
    const checkpointsView = vscode.window.createTreeView('verifrog.checkpoints', {
        treeDataProvider: checkpointsProvider,
    });

    // Register netcoredbg-based debug adapter for the 'verifrog' debug type
    context.subscriptions.push(
        vscode.debug.registerDebugAdapterDescriptorFactory('verifrog', new VerifrogDebugAdapterFactory()),
        vscode.debug.registerDebugConfigurationProvider('verifrog', new VerifrogDebugConfigProvider()),
    );

    // Track debug session lifecycle
    const debugStartListener = vscode.debug.onDidStartDebugSession(session => {
        vscode.commands.executeCommand('setContext', 'verifrog.sessionActive', true);
        // Auto-add function breakpoint on Sim.ReadOrFail.
        // F# computation expression line breakpoints don't resolve correctly in
        // either vsdbg or netcoredbg DAP mode, but function breakpoints work.
        const bp = new vscode.FunctionBreakpoint('Verifrog.Sim.Sim.ReadOrFail');
        vscode.debug.addBreakpoints([bp]);
    });

    const debugEndListener = vscode.debug.onDidTerminateDebugSession(() => {
        vscode.commands.executeCommand('setContext', 'verifrog.sessionActive', false);
        simFrameId = undefined;
        signalsProvider.clear();
        checkpointsProvider.clear();
        vcdTracing = false;
    });

    // Refresh signals when debugger pauses — listen for DAP stopped events
    const trackerFactory = vscode.debug.registerDebugAdapterTrackerFactory('*', {
        createDebugAdapterTracker() {
            return {
                onDidSendMessage(message: any) {
                    if (message.type === 'event' && message.event === 'stopped') {
                        const threadId = message.body?.threadId;
                        setTimeout(() => refreshSignals(threadId), 500);
                    }
                },
            };
        },
    });

    // Commands
    context.subscriptions.push(
        vscode.commands.registerCommand('verifrog.startDebug', startDebugSession),
        vscode.commands.registerCommand('verifrog.stepCycles', stepCycles),
        vscode.commands.registerCommand('verifrog.runUntilSignal', runUntilSignal),
        vscode.commands.registerCommand('verifrog.saveCheckpoint', saveCheckpoint),
        vscode.commands.registerCommand('verifrog.restoreCheckpoint', restoreCheckpoint),
        vscode.commands.registerCommand('verifrog.toggleVcd', toggleVcd),
        vscode.commands.registerCommand('verifrog.refreshSignals', () => refreshSignals()),
        vscode.commands.registerCommand('verifrog.addSignalWatch', addSignalWatch),
        vscode.commands.registerCommand('verifrog.setSignalWatchpoint', setSignalWatchpoint),
        debugStartListener,
        debugEndListener,
        trackerFactory,
        signalsView,
        checkpointsView,
    );
}

export function deactivate() {}

// ---- Debug evaluation helpers ----

let simFrameId: number | undefined;

async function findSimFrame(session: vscode.DebugSession, hintThreadId?: number): Promise<number | undefined> {
    try {
        const threads = await session.customRequest('threads');
        if (!threads.threads?.length) { return undefined; }

        const threadIds: number[] = [];
        if (hintThreadId) { threadIds.push(hintThreadId); }
        for (const t of threads.threads) {
            if (t.id !== hintThreadId) { threadIds.push(t.id); }
        }

        for (const tid of threadIds) {
            try {
                const stack = await session.customRequest('stackTrace', {
                    threadId: tid,
                    startFrame: 0,
                    levels: 20,
                });
                if (!stack.stackFrames?.length) { continue; }

                for (const frame of stack.stackFrames) {
                    const name: string = frame.name || '';
                    if (!name.includes('Verifrog.Sim.Sim')) { continue; }

                    try {
                        const resp = await session.customRequest('evaluate', {
                            expression: 'this.Cycle',
                            frameId: frame.id,
                            context: 'watch',
                        });
                        if (resp.result && !resp.result.includes('error')) {
                            simFrameId = frame.id;
                            return frame.id;
                        }
                    } catch {
                        // not a Sim frame
                    }
                }
            } catch {
                // thread may have exited
            }
        }
        return undefined;
    } catch {
        return undefined;
    }
}

async function evalInSimFrame(expr: string, hintThreadId?: number): Promise<string | undefined> {
    const session = vscode.debug.activeDebugSession;
    if (!session) { return undefined; }

    if (!simFrameId) {
        await findSimFrame(session, hintThreadId);
    }
    if (!simFrameId) { return undefined; }

    try {
        const response = await session.customRequest('evaluate', {
            expression: expr,
            frameId: simFrameId,
            context: 'watch',
        });
        return response.result;
    } catch {
        simFrameId = undefined;
        await findSimFrame(session, hintThreadId);
        if (!simFrameId) { return undefined; }
        try {
            const response = await session.customRequest('evaluate', {
                expression: expr,
                frameId: simFrameId,
                context: 'watch',
            });
            return response.result;
        } catch {
            return undefined;
        }
    }
}

// ---- Commands ----

async function startDebugSession() {
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (!workspaceFolder) {
        vscode.window.showErrorMessage('No workspace folder open');
        return;
    }

    const launchConfig = vscode.workspace.getConfiguration('launch', workspaceFolder.uri);
    const configs = launchConfig.get<any[]>('configurations') || [];
    const verifrogConfig = configs.find(c => c.name?.includes('Debug Tests'));

    if (verifrogConfig) {
        await vscode.debug.startDebugging(workspaceFolder, verifrogConfig);
    } else {
        vscode.window.showInformationMessage(
            'No Verifrog debug configuration found. Run "verifrog init" to generate .vscode/launch.json.'
        );
    }
}

async function stepCycles() {
    const config = vscode.workspace.getConfiguration('verifrog');
    const defaultN = config.get<number>('stepSize', 1);
    const input = await vscode.window.showInputBox({
        prompt: 'Number of clock cycles to step',
        value: String(defaultN),
        validateInput: v => /^\d+$/.test(v) ? null : 'Enter a positive integer',
    });
    if (!input) { return; }

    const result = await evalInSimFrame(`this.StepCycles(${input})`);
    if (result) {
        vscode.window.showInformationMessage(`Stepped ${input} cycles → cycle ${result}`);
    }
    refreshSignals();
}

async function runUntilSignal() {
    const signal = await vscode.window.showInputBox({
        prompt: 'Signal name (hierarchical path)',
        placeHolder: 'e.g. u_fsm.state',
    });
    if (!signal) { return; }

    const value = await vscode.window.showInputBox({
        prompt: `Value to wait for on ${signal}`,
        placeHolder: 'e.g. 1',
    });
    if (value === undefined) { return; }

    const maxCycles = await vscode.window.showInputBox({
        prompt: 'Maximum cycles to wait',
        value: '10000',
    });
    if (!maxCycles) { return; }

    const result = await evalInSimFrame(
        `this.RunUntilSignal("${signal}", ${value}L, ${maxCycles})`
    );
    if (result) {
        vscode.window.showInformationMessage(`RunUntil complete: ${result}`);
    }
    refreshSignals();
}

async function saveCheckpoint() {
    const name = await vscode.window.showInputBox({
        prompt: 'Checkpoint name',
        placeHolder: 'e.g. before_bug',
    });
    if (!name) { return; }

    const result = await evalInSimFrame(`this.Save("${name}")`);
    if (result) {
        vscode.window.showInformationMessage(`Checkpoint "${name}" saved`);
        checkpointsProvider.addCheckpoint(name, result);
    }
}

async function restoreCheckpoint(item?: any) {
    let name: string | undefined;
    if (item?.label) {
        name = item.label;
    } else {
        name = await vscode.window.showInputBox({
            prompt: 'Checkpoint name to restore',
        });
    }
    if (!name) { return; }

    const result = await evalInSimFrame(`this.Restore("${name}")`);
    if (result) {
        vscode.window.showInformationMessage(`Restored "${name}" → cycle ${result}`);
    }
    refreshSignals();
}

let vcdTracing = false;

async function toggleVcd() {
    const config = vscode.workspace.getConfiguration('verifrog');
    const vcdPath = config.get<string>('vcdOutputPath', 'output/debug.vcd');

    if (!vcdTracing) {
        const result = await evalInSimFrame(`this.EnableVcd("${vcdPath}")`);
        if (result !== undefined) {
            vcdTracing = true;
            vscode.window.showInformationMessage(`VCD tracing started: ${vcdPath}`);
        }
    } else {
        await evalInSimFrame('this.DisableVcd()');
        vcdTracing = false;
        vscode.window.showInformationMessage('VCD tracing stopped');

        const surfer = vscode.extensions.getExtension('surfer-project.surfer');
        if (surfer) {
            const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
            if (workspaceFolder) {
                const vcdUri = vscode.Uri.joinPath(workspaceFolder.uri, vcdPath);
                vscode.commands.executeCommand('vscode.open', vcdUri);
            }
        }
    }
}

async function addSignalWatch(item?: SignalItem) {
    if (!item?.signalName) { return; }
    const expr = `sim.ReadOrFail("${item.signalName}")`;
    await vscode.env.clipboard.writeText(expr);
    vscode.window.showInformationMessage(
        `Copied to clipboard: ${expr} — paste in the Watch panel`
    );
}

async function setSignalWatchpoint(item?: SignalItem) {
    let signal: string | undefined;
    if (item?.signalName) {
        signal = item.signalName;
    } else {
        signal = await vscode.window.showInputBox({ prompt: 'Signal name for watchpoint' });
    }
    if (!signal) { return; }

    const value = await vscode.window.showInputBox({
        prompt: `Break when ${signal} equals...`,
        placeHolder: 'e.g. 1',
    });
    if (value === undefined) { return; }

    const condition = `sim.ReadOrFail("${signal}") == ${value}`;
    await vscode.env.clipboard.writeText(condition);
    vscode.window.showInformationMessage(
        `Condition copied: ${condition} — paste as conditional breakpoint`
    );
}

// ---- Signals refresh ----

async function refreshSignals(hintThreadId?: number) {
    const session = vscode.debug.activeDebugSession;
    if (!session) { return; }

    simFrameId = undefined;

    const countStr = await evalInSimFrame('this.SignalCount', hintThreadId);
    if (!countStr) { return; }

    const signalCount = parseInt(countStr, 10);
    if (isNaN(signalCount) || signalCount === 0) { return; }

    const limit = Math.min(signalCount, 50);
    const signals = await getSignalNames(session, limit, hintThreadId);
    if (signals.length === 0) { return; }

    const signalValues: Array<{ name: string; value: string }> = [];
    for (const sig of signals) {
        const val = await evalInSimFrame(`this.ReadOrFail("${sig}")`);
        signalValues.push({ name: sig, value: val || '?' });
    }

    const cycle = await evalInSimFrame('this.Cycle');
    signalsProvider.update(signalValues, cycle || '?');
}

async function getSignalNames(
    session: vscode.DebugSession,
    limit: number,
    hintThreadId?: number,
): Promise<string[]> {
    if (!simFrameId) {
        await findSimFrame(session, hintThreadId);
    }
    if (!simFrameId) { return []; }

    // Try expanding array via variablesReference (single DAP call)
    try {
        const arrResult = await session.customRequest('evaluate', {
            expression: 'System.Linq.Enumerable.ToArray(this.ListSignals())',
            frameId: simFrameId,
            context: 'watch',
        });

        if (arrResult.variablesReference && arrResult.variablesReference > 0) {
            const vars = await session.customRequest('variables', {
                variablesReference: arrResult.variablesReference,
                start: 0,
                count: limit,
            });
            const signals: string[] = [];
            for (const v of vars.variables || []) {
                if (v.value) {
                    signals.push(v.value.replace(/^"|"$/g, ''));
                }
            }
            if (signals.length > 0) { return signals; }
        }
    } catch {
        // fall through to per-index approach
    }

    // Fallback: index one at a time
    const signals: string[] = [];
    for (let i = 0; i < limit; i++) {
        const name = await evalInSimFrame(
            `System.Linq.Enumerable.ToArray(this.ListSignals())[${i}]`
        );
        if (name) {
            signals.push(name.replace(/^"|"$/g, ''));
        }
    }
    return signals;
}
