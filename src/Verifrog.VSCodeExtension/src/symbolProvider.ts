import * as vscode from 'vscode';

/**
 * Provides document symbols (outline) for .verifrog files.
 * Each `test "name" [Category]:` becomes a symbol.
 */
export class VerifrogDocumentSymbolProvider implements vscode.DocumentSymbolProvider {
    provideDocumentSymbols(document: vscode.TextDocument): vscode.DocumentSymbol[] {
        const symbols: vscode.DocumentSymbol[] = [];
        const testRegex = /^\s*test\s+"([^"]+)"\s*(?:\[(\w+)\])?\s*:/;

        for (let i = 0; i < document.lineCount; i++) {
            const line = document.lineAt(i);
            const match = testRegex.exec(line.text);
            if (!match) { continue; }

            const name = match[1];
            const category = match[2];

            // Find the range: from this test line to the next test line (or EOF)
            let endLine = document.lineCount - 1;
            for (let j = i + 1; j < document.lineCount; j++) {
                if (testRegex.test(document.lineAt(j).text)) {
                    endLine = j - 1;
                    // Skip trailing blank lines
                    while (endLine > i && document.lineAt(endLine).isEmptyOrWhitespace) {
                        endLine--;
                    }
                    break;
                }
            }

            const range = new vscode.Range(i, 0, endLine, document.lineAt(endLine).text.length);
            const detail = category ? `[${category}]` : '';

            const symbol = new vscode.DocumentSymbol(
                name,
                detail,
                vscode.SymbolKind.Function,
                range,
                line.range,
            );

            symbols.push(symbol);
        }

        return symbols;
    }
}
