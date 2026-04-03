import * as vscode from 'vscode';

class CheckpointItem extends vscode.TreeItem {
    constructor(name: string, detail: string) {
        super(name, vscode.TreeItemCollapsibleState.None);
        this.description = detail;
        this.tooltip = `Checkpoint "${name}" — ${detail}`;
        this.contextValue = 'checkpoint';
        this.iconPath = new vscode.ThemeIcon('bookmark');
    }
}

export class CheckpointsProvider implements vscode.TreeDataProvider<CheckpointItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<CheckpointItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private checkpoints: Array<{ name: string; detail: string }> = [];

    addCheckpoint(name: string, detail: string) {
        // Replace if exists
        this.checkpoints = this.checkpoints.filter(c => c.name !== name);
        this.checkpoints.push({ name, detail });
        this._onDidChangeTreeData.fire(undefined);
    }

    clear() {
        this.checkpoints = [];
        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: CheckpointItem): CheckpointItem {
        return element;
    }

    getChildren(): CheckpointItem[] {
        if (this.checkpoints.length === 0) {
            const placeholder = new CheckpointItem('No checkpoints', 'Use "Verifrog: Save Checkpoint"');
            placeholder.iconPath = new vscode.ThemeIcon('info');
            placeholder.contextValue = 'placeholder';
            return [placeholder];
        }
        return this.checkpoints.map(c => new CheckpointItem(c.name, c.detail));
    }
}
