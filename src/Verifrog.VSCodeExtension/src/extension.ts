import * as vscode from 'vscode';
import { SignalsProvider, SignalItem } from './signals';
import { CheckpointsProvider } from './checkpoints';
import { DebugSession } from './debugSession';
import { VerifrogDocumentSymbolProvider } from './symbolProvider';

let debugSession: DebugSession | undefined;
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

    // Track debug session lifecycle
    const debugListener = vscode.debug.onDidStartDebugSession(session => {
        vscode.commands.executeCommand('setContext', 'verifrog.sessionActive', true);
    });
    const debugEndListener = vscode.debug.onDidTerminateDebugSession(session => {
        vscode.commands.executeCommand('setContext', 'verifrog.sessionActive', false);
        debugSession = undefined;
        signalsProvider.clear();
        checkpointsProvider.clear();
    });

    // Refresh signals when debugger pauses — use DebugAdapterTracker to catch 'stopped' events
    const trackerFactory = vscode.debug.registerDebugAdapterTrackerFactory('*', {
        createDebugAdapterTracker(session: vscode.DebugSession) {
            console.log('[verifrog] tracker created for session:', session.type, session.name);
            return {
                onDidSendMessage(message: any) {
                    if (message.type === 'event' && message.event === 'stopped') {
                        const threadId = message.body?.threadId;
                        console.log('[verifrog] stopped event on thread', threadId);
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
        debugListener,
        debugEndListener,
        trackerFactory,
        signalsView,
        checkpointsView,
    );
}

export function deactivate() {
    debugSession = undefined;
}

function isVerifrogSession(session: vscode.DebugSession): boolean {
    return session.type === 'coreclr' &&
        session.configuration?.env?.DYLD_LIBRARY_PATH !== undefined;
}

// ---- Debug evaluation helpers ----

async function evalExpression(expr: string): Promise<string | undefined> {
    const session = vscode.debug.activeDebugSession;
    if (!session) {
        vscode.window.showWarningMessage('No active debug session');
        return undefined;
    }
    try {
        const response = await session.customRequest('evaluate', {
            expression: expr,
            context: 'watch',
        });
        return response.result;
    } catch (e: any) {
        return undefined;
    }
}

// Cache the thread/frame where sim is in scope
let simThreadId: number | undefined;
let simFrameId: number | undefined;

async function findSimFrame(session: vscode.DebugSession, hintThreadId?: number): Promise<number | undefined> {
    try {
        const threads = await session.customRequest('threads');
        if (!threads.threads?.length) { return undefined; }

        // Try the hint thread first, then all threads
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

                // Walk frames looking for one where we're inside a Sim method
                // (where 'this' is a Verifrog.Sim.Sim instance)
                for (const frame of stack.stackFrames) {
                    // Quick check: does the frame name suggest it's in Sim code?
                    const name: string = frame.name || '';
                    const inSimCode = name.includes('Verifrog.Sim.Sim') ||
                                      name.includes('SimTests');
                    if (!inSimCode) { continue; }

                    try {
                        // Try evaluating 'this.Cycle' — works in Sim instance methods
                        const resp = await session.customRequest('evaluate', {
                            expression: 'this.Cycle',
                            frameId: frame.id,
                            context: 'watch',
                        });
                        if (resp.result && !resp.result.includes('error')) {
                            console.log('[verifrog] found Sim context in thread', tid, 'frame', frame.id, frame.name);
                            simThreadId = tid;
                            simFrameId = frame.id;
                            return frame.id;
                        }
                    } catch {
                        // not a Sim frame — try next
                    }
                }
            } catch {
                // thread may have exited — skip
            }
        }
        console.log('[verifrog] sim not found in any frame');
        return undefined;
    } catch (e: any) {
        console.error('[verifrog] findSimFrame error:', e?.message || e);
        return undefined;
    }
}

async function evalExpressionInFrame(expr: string, hintThreadId?: number): Promise<string | undefined> {
    const session = vscode.debug.activeDebugSession;
    if (!session) { return undefined; }

    // Find the frame with sim if we don't have one cached
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
        console.log('[verifrog] eval:', expr, '=>', response.result?.substring(0, 80));
        return response.result;
    } catch (e: any) {
        // Frame may be stale — clear cache and retry once
        console.log('[verifrog] eval failed, clearing frame cache:', e?.message);
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

    // Look for launch.json config or create one
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

    const result = await evalExpressionInFrame(`sim.StepCycles(${input})`);
    if (result) {
        vscode.window.showInformationMessage(`Stepped ${input} cycles (now at cycle ${result})`);
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

    const result = await evalExpressionInFrame(
        `sim.RunUntilSignal("${signal}", ${value}L, ${maxCycles})`
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

    const result = await evalExpressionInFrame(`sim.Save("${name}")`);
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

    const result = await evalExpressionInFrame(`sim.Restore("${name}")`);
    if (result) {
        vscode.window.showInformationMessage(`Restored checkpoint "${name}" (cycle ${result})`);
    }
    refreshSignals();
}

let vcdTracing = false;

async function toggleVcd() {
    const config = vscode.workspace.getConfiguration('verifrog');
    const vcdPath = config.get<string>('vcdOutputPath', 'output/debug.vcd');

    if (!vcdTracing) {
        const result = await evalExpressionInFrame(`sim.EnableVcd("${vcdPath}")`);
        if (result !== undefined) {
            vcdTracing = true;
            vscode.window.showInformationMessage(`VCD tracing started: ${vcdPath}`);
        }
    } else {
        await evalExpressionInFrame(`sim.DisableVcd()`);
        vcdTracing = false;
        vscode.window.showInformationMessage('VCD tracing stopped');

        // Try to open in Surfer extension
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
    // Add the signal as a debug watch expression
    vscode.debug.addBreakpoints([]);  // no-op to ensure debug UI is open
    // The best we can do is copy to clipboard and instruct user, or use debug.addWatchExpression if available
    const expr = `sim.ReadOrFail("${item.signalName}")`;
    await vscode.env.clipboard.writeText(expr);
    vscode.window.showInformationMessage(
        `Copied watch expression to clipboard: ${expr}\n` +
        `Paste it in the Watch panel (Ctrl+Shift+D → Watch → +)`
    );
}

async function setSignalWatchpoint(item?: SignalItem) {
    let signal: string | undefined;
    if (item?.signalName) {
        signal = item.signalName;
    } else {
        signal = await vscode.window.showInputBox({
            prompt: 'Signal name for watchpoint',
        });
    }
    if (!signal) { return; }

    const value = await vscode.window.showInputBox({
        prompt: `Break when ${signal} equals...`,
        placeHolder: 'e.g. 1',
    });
    if (value === undefined) { return; }

    // Guide the user — we can't programmatically set conditional breakpoints
    // on arbitrary lines, but we can show them how
    const condition = `sim.ReadOrFail("${signal}") == ${value}`;
    await vscode.env.clipboard.writeText(condition);
    vscode.window.showInformationMessage(
        `Condition copied to clipboard: ${condition}\n` +
        `Set a breakpoint on a sim.Step() line, right-click → Edit Breakpoint → paste condition.`
    );
}

// ---- Signals refresh ----

async function refreshSignals(hintThreadId?: number) {
    console.log('[verifrog] refreshSignals called, hintThread:', hintThreadId);
    const session = vscode.debug.activeDebugSession;
    if (!session) { return; }

    // Clear frame cache on each refresh so we re-discover the right frame
    simFrameId = undefined;

    // Get signal count first
    const countStr = await evalExpressionInFrame('this.SignalCount', hintThreadId);
    console.log('[verifrog] SignalCount:', countStr);
    if (!countStr) { return; }

    const signalCount = parseInt(countStr, 10);
    if (isNaN(signalCount) || signalCount === 0) { return; }

    // Get signal names by converting list to array and indexing
    // vsdbg can't expand F# lists inline, so get as array
    const listArr = await evalExpressionInFrame(
        'System.Linq.Enumerable.ToArray(this.ListSignals())', hintThreadId
    );
    console.log('[verifrog] listArr:', listArr?.substring(0, 120));

    // If array works, extract signal names by indexing
    const signals: string[] = [];
    const limit = Math.min(signalCount, 50);
    for (let i = 0; i < limit; i++) {
        const name = await evalExpressionInFrame(
            `System.Linq.Enumerable.ToArray(this.ListSignals())[${i}]`
        );
        if (name) {
            // vsdbg wraps strings in quotes: "TOP.counter.count"
            const clean = name.replace(/^"|"$/g, '');
            signals.push(clean);
        }
    }
    console.log('[verifrog] got', signals.length, 'signal names');

    if (signals.length === 0) { return; }

    // Read current values
    const signalValues: Array<{ name: string; value: string }> = [];
    for (const sig of signals) {
        const val = await evalExpressionInFrame(`this.ReadOrFail("${sig}")`);
        signalValues.push({ name: sig, value: val || '?' });
    }

    // Get cycle
    const cycle = await evalExpressionInFrame('this.Cycle');

    signalsProvider.update(signalValues, cycle || '?');
}

function parseSignalList(raw: string): string[] {
    // netcoredbg returns F# list as nested structure like:
    // {FSharpList<string>}: {head = "sig1", tail = {head = "sig2", ...}}
    const signals: string[] = [];
    const headRegex = /head\s*=\s*"([^"]+)"/g;
    let match;
    while ((match = headRegex.exec(raw)) !== null) {
        signals.push(match[1]);
    }
    return signals;
}
