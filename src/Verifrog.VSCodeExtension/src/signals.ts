import * as vscode from 'vscode';

export class SignalItem extends vscode.TreeItem {
    public signalName: string;

    constructor(name: string, value: string) {
        // Show short name (last component) with full value
        const shortName = name.split('.').pop() || name;
        super(`${shortName} = ${value}`, vscode.TreeItemCollapsibleState.None);
        this.signalName = name;
        this.tooltip = `${name} = ${value}`;
        this.description = name;
        this.contextValue = 'signal';
        this.iconPath = new vscode.ThemeIcon('symbol-variable');
    }
}

class SignalGroupItem extends vscode.TreeItem {
    public children: SignalItem[] = [];

    constructor(name: string) {
        super(name, vscode.TreeItemCollapsibleState.Expanded);
        this.iconPath = new vscode.ThemeIcon('symbol-namespace');
        this.contextValue = 'signalGroup';
    }
}

export class SignalsProvider implements vscode.TreeDataProvider<vscode.TreeItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<vscode.TreeItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private signals: Array<{ name: string; value: string }> = [];
    private cycle: string = '?';

    update(signals: Array<{ name: string; value: string }>, cycle: string) {
        this.signals = signals;
        this.cycle = cycle;
        this._onDidChangeTreeData.fire(undefined);
    }

    clear() {
        this.signals = [];
        this.cycle = '?';
        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: vscode.TreeItem): vscode.TreeItem {
        return element;
    }

    getChildren(element?: vscode.TreeItem): vscode.TreeItem[] {
        if (element instanceof SignalGroupItem) {
            return element.children;
        }

        if (element) {
            return [];
        }

        // Root level: group signals by hierarchy prefix
        if (this.signals.length === 0) {
            const placeholder = new vscode.TreeItem(
                'No signals (start a debug session first)',
                vscode.TreeItemCollapsibleState.None,
            );
            placeholder.iconPath = new vscode.ThemeIcon('info');
            return [placeholder];
        }

        // Cycle count header
        const cycleItem = new vscode.TreeItem(
            `Cycle: ${this.cycle}`,
            vscode.TreeItemCollapsibleState.None,
        );
        cycleItem.iconPath = new vscode.ThemeIcon('clock');

        // Group by first hierarchy component (e.g., TOP.counter.*)
        const groups = new Map<string, SignalItem[]>();
        for (const sig of this.signals) {
            const parts = sig.name.split('.');
            const group = parts.length > 2
                ? parts.slice(0, 2).join('.')
                : parts[0];
            if (!groups.has(group)) {
                groups.set(group, []);
            }
            groups.get(group)!.push(new SignalItem(sig.name, sig.value));
        }

        const items: vscode.TreeItem[] = [cycleItem];

        if (groups.size === 1) {
            // Single group — flatten
            for (const [, signals] of groups) {
                items.push(...signals);
            }
        } else {
            for (const [name, signals] of groups) {
                const group = new SignalGroupItem(name);
                group.children = signals;
                group.description = `${signals.length} signals`;
                items.push(group);
            }
        }

        return items;
    }
}
