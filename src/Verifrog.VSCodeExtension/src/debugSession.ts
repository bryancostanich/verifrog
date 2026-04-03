import * as vscode from 'vscode';

/**
 * Placeholder for future custom debug adapter integration.
 * Currently, Verifrog uses the standard coreclr debug type from the C# extension.
 * This module is reserved for Phase 4 (Claude MCP integration) where we may need
 * a custom debug adapter that wraps netcoredbg with structured JSON responses.
 */
export class DebugSession {
    private _active = false;

    get active(): boolean {
        return this._active;
    }

    start() {
        this._active = true;
    }

    stop() {
        this._active = false;
    }
}
