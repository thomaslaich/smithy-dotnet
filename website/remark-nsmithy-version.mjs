// Remark plugin: substitute the `NSMITHY_VERSION` placeholder in docs with the
// current NSmithy version, read from the repo-root VERSION file (the single
// source of truth). Lets docs pin package versions without hardcoding a number
// that drifts every release — write `Version="NSMITHY_VERSION"` and the built
// site renders the real version, so copy-pasted snippets are always current.
import { readFileSync } from 'node:fs';

const PLACEHOLDER = /NSMITHY_VERSION/g;

// Read once at config load. rootDir is website/, VERSION lives at the repo root.
const version = readFileSync(new URL('../VERSION', import.meta.url), 'utf-8').trim();

export function remarkNSmithyVersion() {
	return (tree) => {
		const visit = (node) => {
			// text, inlineCode and code nodes all carry a literal `value` string.
			if (typeof node.value === 'string' && PLACEHOLDER.test(node.value)) {
				node.value = node.value.replace(PLACEHOLDER, version);
			}
			if (Array.isArray(node.children)) {
				for (const child of node.children) visit(child);
			}
		};
		visit(tree);
	};
}
