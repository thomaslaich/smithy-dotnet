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
			expressiveCode: {
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
