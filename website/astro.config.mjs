// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLlmsTxt from 'starlight-llms-txt';
import mermaid from 'astro-mermaid';
import { readFileSync } from 'node:fs';

const smithyGrammar = JSON.parse(
	readFileSync(new URL('./src/smithy.tmLanguage.json', import.meta.url), 'utf-8')
);

// https://astro.build/config
export default defineConfig({
	site: 'https://thomaslaich.github.io',
	base: '/smithy-dotnet',
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
						href: 'https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=JetBrains+Mono:wght@400;500;600&family=Space+Grotesk:wght@500;600;700&display=swap',
					},
				},
			],
			plugins: [
				// Emit /llms.txt (curated index) and /llms-full.txt (full docs) for LLM consumption.
				// https://llmstxt.org · https://github.com/HiDeoo/starlight-llms-txt
				starlightLlmsTxt({
					projectName: 'NSmithy',
					description:
						'NSmithy generates C# models, typed HTTP clients, and ASP.NET Core minimal-API servers from Smithy models at build time — no separate codegen step and no JRE required by consumers.',
				}),
			],
			logo: {
				src: './src/assets/brand/nsmithy_logo_1.png',
				alt: 'NSmithy logo',
				replacesTitle: false,
			},
			social: [
				{ icon: 'github', label: 'GitHub', href: 'https://github.com/thomaslaich/smithy-dotnet' },
			],
			editLink: {
				baseUrl: 'https://github.com/thomaslaich/smithy-dotnet/edit/main/website/',
			},
			// Replace the default theme dropdown with the landing's sun/moon toggle.
			components: {
				ThemeSelect: './src/components/StarlightThemeToggle.astro',
			},
			// Shared code-block styling (src/styles/code.css) + landing/docs theme
			// alignment — fonts and a near-black dark mode (src/styles/docs-theme.css).
			customCss: ['./src/styles/code.css', './src/styles/docs-theme.css'],
			expressiveCode: {
				// Match TypeHintCode's highlighter (github-dark / github-light) so
				// token colours and code backgrounds are identical across both.
				themes: ['github-dark', 'github-light'],
				shiki: {
					langs: [smithyGrammar],
				},
			},
			sidebar: [
				{
					label: 'Overview',
					items: [
						{ label: 'Introduction', slug: 'getting-started/introduction' },
						{ label: 'Quick Start', slug: 'getting-started/quick-start' },
						{ label: 'Modeling', slug: 'guides/modeling' },
						{
							label: 'Client Configuration',
							items: [
								{ label: 'Overview', slug: 'guides/client-configuration' },
								{ label: 'Authentication', slug: 'guides/client-configuration/authentication' },
								{ label: 'Retry', slug: 'guides/client-configuration/retry' },
								{ label: 'Interceptors', slug: 'guides/client-configuration/interceptors' },
								{ label: 'Transport', slug: 'guides/client-configuration/transport' },
								{ label: 'Dependency Injection', slug: 'guides/client-configuration/dependency-injection' },
							],
						},
						{ label: 'Distributing Contracts', slug: 'guides/distributing-contracts' },
						{ label: 'Endpoint Documentation', slug: 'guides/endpoint-documentation' },
					],
				},
				{
					label: 'Protocols',
					items: [
						{ label: 'Overview', slug: 'protocols/overview' },
						{ label: 'Client & Server Usage', slug: 'protocols/usage' },
						{ label: 'simpleRestJson', slug: 'protocols/simple-rest-json' },
						{ label: 'RPC v2 CBOR', slug: 'protocols/rpc-v2-cbor' },
						{ label: 'gRPC', slug: 'protocols/grpc' },
						{
							label: 'AWS Protocols',
							items: [
								{ label: 'Overview', slug: 'protocols/aws-overview' },
								{ label: 'AWS restJson1', slug: 'protocols/aws-rest-json1' },
								{ label: 'AWS JSON', slug: 'protocols/aws-json' },
								{ label: 'AWS restXml', slug: 'protocols/rest-xml' },
							],
						},
						{ label: 'Protocol Status', slug: 'protocols/status' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'MSBuild', slug: 'reference/msbuild' },
						{ label: 'Known Limitations', slug: 'reference/known-limitations' },
						{ label: 'Design Docs', slug: 'reference/design' },
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
