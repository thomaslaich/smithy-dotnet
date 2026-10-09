// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLlmsTxt from 'starlight-llms-txt';
import mermaid from 'astro-mermaid';
import { readFileSync } from 'node:fs';
import { remarkNSmithyVersion, version } from './remark-nsmithy-version.mjs';
import { generateChangelogPage } from './changelog-page.mjs';

// Render the repo-root CHANGELOG.md as the Reference > Changelog page.
generateChangelogPage();

const smithyGrammar = JSON.parse(
	readFileSync(new URL('./src/smithy.tmLanguage.json', import.meta.url), 'utf-8')
);

// https://astro.build/config
export default defineConfig({
	site: 'https://thomaslaich.github.io',
	base: '/smithy-dotnet',
	// Substitute the NSMITHY_VERSION placeholder in docs with the repo-root VERSION.
	markdown: {
		remarkPlugins: [remarkNSmithyVersion],
	},
	// Expose the same version to components (the header's version link).
	vite: {
		define: { __NSMITHY_VERSION__: JSON.stringify(version) },
	},
	integrations: [
		mermaid({ autoTheme: true }),
		starlight({
			title: 'NSmithy',
			description: 'Generate C# models, typed HTTP clients, and ASP.NET Core servers from Smithy models.',
			// Load the landing-page fonts globally so the docs share the same type.
			head: [
				{ tag: 'link', attrs: { rel: 'preconnect', href: 'https://fonts.googleapis.com' } },
				{ tag: 'link', attrs: { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: true } },
				{
					tag: 'link',
					attrs: {
						rel: 'stylesheet',
						href: 'https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600;700&family=Geist+Mono:wght@400;500;600&family=IBM+Plex+Sans:wght@400;500;600&family=JetBrains+Mono:wght@400;500;600&family=Space+Grotesk:wght@500;600;700&display=swap',
					},
				},
			],
			plugins: [
				// Emit /llms.txt (curated index) and /llms-full.txt (full docs) for LLM consumption.
				// https://llmstxt.org · https://github.com/HiDeoo/starlight-llms-txt
				starlightLlmsTxt({
					projectName: 'NSmithy',
					description:
						'NSmithy generates C# models, typed HTTP clients, and ASP.NET Core minimal-API servers from Smithy models at build time. Consumers need no separate codegen step or JRE.',
				}),
			],
			social: [
				{ icon: 'github', label: 'GitHub', href: 'https://github.com/thomaslaich/smithy-dotnet' },
			],
			editLink: {
				baseUrl: 'https://github.com/thomaslaich/smithy-dotnet/edit/main/website/',
			},
			// Replace the default theme dropdown with the landing's sun/moon toggle, and
			// append the current version to the site title.
			components: {
				ThemeSelect: './src/components/StarlightThemeToggle.astro',
				SiteTitle: './src/components/SiteTitle.astro',
			},
			// Shared code-block styling (src/styles/code.css), plus landing/docs theme
			// alignment, fonts, and a near-black dark mode (src/styles/docs-theme.css).
			customCss: ['./src/styles/code.css', './src/styles/docs-theme.css'],
			expressiveCode: {
				// Match TypeHintCode's highlighter (github-dark / github-light) so
				// token colours and code backgrounds are identical across both.
				themes: ['github-dark', 'github-light'],
				// Untitled shell snippets render as plain code frames instead of
				// terminal windows with an empty title bar.
				defaultProps: {
					overridesByLang: {
						'bash,console,powershell,ps,sh,shell,zsh': { frame: 'code' },
					},
				},
				shiki: {
					langs: [smithyGrammar],
				},
			},
			sidebar: [
				{
					label: 'Getting started',
					items: [
						{ label: 'Introduction', slug: 'getting-started/introduction' },
						{ label: 'Quick start', slug: 'getting-started/quick-start' },
					],
				},
				{
					label: 'Concepts',
					items: [
						{ label: 'Modeling contracts', slug: 'concepts/modeling' },
						{ label: 'Code generation', slug: 'concepts/code-generation' },
						{ label: 'Smithy and TypeSpec', slug: 'concepts/smithy-and-typespec' },
					],
				},
				{
					label: 'Clients',
					items: [
						{ label: 'Overview', slug: 'guides/client-configuration' },
						{ label: 'Authentication', slug: 'guides/client-configuration/authentication' },
						{ label: 'Retry', slug: 'guides/client-configuration/retry' },
						{ label: 'Interceptors', slug: 'guides/client-configuration/interceptors' },
						{ label: 'Observability', slug: 'guides/client-configuration/observability' },
						{ label: 'Pagination', slug: 'guides/client-configuration/pagination' },
						{ label: 'Transport', slug: 'guides/client-configuration/transport' },
						{ label: 'Dependency injection', slug: 'guides/client-configuration/dependency-injection' },
						{ label: 'Fake clients', slug: 'guides/client-configuration/fake-clients' },
					],
				},
				{
					label: 'Servers',
					items: [
						{ label: 'Overview', slug: 'servers' },
						{ label: 'Validation', slug: 'servers/validation' },
						{ label: 'Hosting multiple protocols', slug: 'servers/hosting' },
						{ label: 'Fake handlers', slug: 'servers/fake-handlers' },
						{ label: 'MCP', slug: 'servers/mcp' },
						{ label: 'Endpoint documentation', slug: 'guides/endpoint-documentation' },
					],
				},
				{
					label: 'Protocols',
					items: [
						{ label: 'Overview', slug: 'protocols/overview' },
						{ label: 'REST JSON', slug: 'protocols/rest-json', badge: { text: 'Stable', variant: 'success' } },
						{ label: 'RPC v2 CBOR', slug: 'protocols/rpc-v2-cbor', badge: { text: 'Stable', variant: 'success' } },
						{ label: 'RPC v2 JSON', slug: 'protocols/rpc-v2-json', badge: { text: 'Preview', variant: 'note' } },
						{
							label: 'AWS protocols',
							items: [
								{ label: 'Overview', slug: 'protocols/aws-overview' },
								{ label: 'AWS JSON', slug: 'protocols/aws-json', badge: { text: 'Early preview', variant: 'caution' } },
								{ label: 'AWS Query', slug: 'protocols/aws-query', badge: { text: 'Preview', variant: 'note' } },
								{ label: 'AWS EC2 Query', slug: 'protocols/aws-ec2-query', badge: { text: 'Preview', variant: 'note' } },
								{ label: 'AWS restXml', slug: 'protocols/rest-xml', badge: { text: 'Preview', variant: 'note' } },
							],
						},
						{ label: 'gRPC', slug: 'protocols/grpc', badge: { text: 'Experimental', variant: 'danger' } },
						{ label: 'Protocol status', slug: 'protocols/status' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'MSBuild', slug: 'reference/msbuild' },
						{ label: 'Distributing contracts', slug: 'guides/distributing-contracts' },
						{ label: 'Known limitations', slug: 'reference/known-limitations' },
						{ label: 'Changelog', slug: 'reference/changelog' },
						{ label: 'Design docs', slug: 'reference/design' },
					],
				},
				{
					label: 'Contributing',
					items: [
						{ label: 'Development', slug: 'contributing/development' },
						{ label: 'Roadmap', slug: 'contributing/roadmap' },
						{ label: 'Releasing', slug: 'contributing/releasing' },
					],
				},
			],
		}),
	],
});
