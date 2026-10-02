// Generate the docs Changelog page from the repo-root CHANGELOG.md, the single
// source of truth. Runs at config load, so `astro dev` picks up changelog edits
// on restart. The output is gitignored.
//
// The page drops the changelog's own `# Changelog` heading (Starlight renders
// the title), turns `## [x.y.z]` link headings into plain `## x.y.z` headings so
// they get clean anchors (`#0101`), and adds a line under each release linking
// to its GitHub release and to the compare diff from the reference definitions.
import { readFileSync, writeFileSync } from 'node:fs';

const REPO = 'https://github.com/thomaslaich/smithy-dotnet';

export function generateChangelogPage() {
	const source = readFileSync(new URL('../CHANGELOG.md', import.meta.url), 'utf-8');

	// `[0.10.1]: https://…/compare/v0.10.0...v0.10.1`
	const compareUrls = new Map(
		[...source.matchAll(/^\[([^\]]+)\]:\s*(\S+)\s*$/gm)].map((m) => [m[1], m[2]])
	);

	const body = source
		.replace(/^# Changelog\s*\n/, '')
		.replace(/^## \[([^\]]+)\][ \t]*$/gm, (_, version) => {
			const compare = compareUrls.get(version);
			if (version === 'Unreleased') {
				return compare ? `## Unreleased\n\n[Changes since the last release](${compare})` : '## Unreleased';
			}
			const previous = compare?.match(/compare\/v([^.]+\.[^.]+\.[^.]+)\.\.\./)?.[1];
			const links = [`[GitHub release](${REPO}/releases/tag/v${version})`];
			if (compare && previous) links.push(`[Changes since ${previous}](${compare})`);
			return `## ${version}\n\n${links.join(' · ')}`;
		});

	const page = `---
title: Changelog
description: Notable changes in each NSmithy release.
editUrl: ${REPO}/edit/main/CHANGELOG.md
---

<!-- Generated from the repo-root CHANGELOG.md by website/changelog-page.mjs. Do not edit. -->

${body}`;

	writeFileSync(new URL('./src/content/docs/reference/changelog.md', import.meta.url), page);
}
