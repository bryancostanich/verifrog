import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { execSync } from 'child_process';

/**
 * Provides a DebugAdapterDescriptor that launches netcoredbg in DAP mode.
 * netcoredbg resolves F# computation expression breakpoints correctly
 * (unlike vsdbg), making it the right debugger for Verifrog test projects.
 */
export class VerifrogDebugAdapterFactory implements vscode.DebugAdapterDescriptorFactory {
    createDebugAdapterDescriptor(
        _session: vscode.DebugSession,
        _executable: vscode.DebugAdapterExecutable | undefined,
    ): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
        const ncdbgPath = findNetcoredbg();
        if (!ncdbgPath) {
            vscode.window.showErrorMessage(
                'netcoredbg not found. Install it with: verifrog install --debugger\n' +
                'Or set verifrog.netcoredbgPath in settings.'
            );
            return undefined;
        }

        return new vscode.DebugAdapterExecutable(ncdbgPath, ['--interpreter=vscode']);
    }
}

/**
 * Resolves launch configuration before passing to the debug adapter.
 * Converts our simplified config into the full DAP launch request that
 * netcoredbg expects.
 */
export class VerifrogDebugConfigProvider implements vscode.DebugConfigurationProvider {
    resolveDebugConfiguration(
        folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration,
    ): vscode.ProviderResult<vscode.DebugConfiguration> {
        if (!config.program) {
            vscode.window.showErrorMessage('No program specified in launch configuration');
            return undefined;
        }

        const workspaceDir = folder?.uri.fsPath || '.';
        const resolveVars = (s: string) =>
            s.replace(/\$\{workspaceFolder\}/g, workspaceDir);

        const workDir = resolveVars(config.cwd || workspaceDir);
        let program = resolveVars(config.program);

        // If program points to a .fsproj, find the DLL (build if missing)
        if (program.endsWith('.fsproj')) {
            const projPath = path.isAbsolute(program) ? program : path.join(workDir, program);
            const projName = path.basename(projPath, '.fsproj');
            const dllPath = path.join(
                path.dirname(projPath), 'bin', 'Debug', 'net8.0', `${projName}.dll`
            );

            if (!fs.existsSync(dllPath)) {
                vscode.window.showInformationMessage('Building test project...');
                try {
                    execSync(`dotnet build "${projPath}" -c Debug --nologo -q`, {
                        cwd: workDir,
                        timeout: 60000,
                    });
                } catch {
                    vscode.window.showErrorMessage(
                        `Test DLL not found: ${dllPath}\nRun "dotnet build ${projPath} -c Debug" first.`
                    );
                    return undefined;
                }
            }
            program = dllPath;
        }

        // Build the library path for native sim library
        const env = config.env || {};
        const buildDir = findBuildDir(workDir);
        if (buildDir) {
            const libExt = process.platform === 'darwin' ? 'dylib' : 'so';
            const libPath = path.join(buildDir, `libverifrog_sim.${libExt}`);

            // Symlink native lib next to the DLL (macOS SIP workaround)
            const dllDir = path.dirname(program);
            const symlinkPath = path.join(dllDir, `libverifrog_sim.${libExt}`);
            if (fs.existsSync(libPath) && !fs.existsSync(symlinkPath)) {
                try {
                    fs.symlinkSync(libPath, symlinkPath);
                } catch {
                    // may already exist or permissions issue — not fatal
                }
            }

            if (process.platform === 'darwin') {
                env['DYLD_LIBRARY_PATH'] = buildDir + (env['DYLD_LIBRARY_PATH'] ? ':' + env['DYLD_LIBRARY_PATH'] : '');
            } else {
                env['LD_LIBRARY_PATH'] = buildDir + (env['LD_LIBRARY_PATH'] ? ':' + env['LD_LIBRARY_PATH'] : '');
            }
        }

        // netcoredbg DAP launch config
        return {
            type: 'verifrog',
            name: config.name,
            request: 'launch',
            program,
            args: config.args || [],
            cwd: workDir,
            env,
            stopAtEntry: config.stopAtEntry || false,
            justMyCode: false,
        };
    }
}

function findNetcoredbg(): string | undefined {
    // Check settings first
    const configPath = vscode.workspace.getConfiguration('verifrog').get<string>('netcoredbgPath');
    if (configPath && fs.existsSync(configPath)) {
        return configPath;
    }

    // Check common locations
    const home = process.env.HOME || '';
    const candidates = [
        path.join(home, '.local', 'bin', 'netcoredbg', 'netcoredbg'),
        '/usr/local/bin/netcoredbg',
        '/usr/bin/netcoredbg',
    ];

    for (const c of candidates) {
        if (fs.existsSync(c)) { return c; }
    }

    // Check PATH
    try {
        const result = execSync('which netcoredbg', { encoding: 'utf8', timeout: 5000 });
        const p = result.trim();
        if (p && fs.existsSync(p)) { return p; }
    } catch {
        // not on PATH
    }

    return undefined;
}

function findBuildDir(workDir: string): string | undefined {
    // Walk up looking for verifrog.toml, then read build dir
    let dir = workDir;
    while (dir !== path.dirname(dir)) {
        const toml = path.join(dir, 'verifrog.toml');
        if (fs.existsSync(toml)) {
            // Simple parse: look for output = "build"
            const content = fs.readFileSync(toml, 'utf8');
            const match = /output\s*=\s*"([^"]+)"/.exec(content);
            const buildName = match ? match[1] : 'build';
            const buildDir = path.join(dir, buildName);
            if (fs.existsSync(buildDir)) { return buildDir; }
        }
        dir = path.dirname(dir);
    }
    return undefined;
}
