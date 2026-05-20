// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
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
					label: 'Getting Started',
					items: [
						{ label: 'Quick Start', slug: 'getting-started/quick-start' },
						{ label: 'Environment Setup', slug: 'getting-started/environment' },
					],
				},
				{
					label: 'Guides',
					items: [
						{ label: 'Multi-Protocol', slug: 'guides/multi-protocol' },
						{ label: 'Contracts Project', slug: 'guides/contracts-project' },
						{ label: 'Distributing Contracts', slug: 'guides/distributing-contracts' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'MSBuild', slug: 'reference/msbuild' },
						{ label: 'Supported Surface', slug: 'reference/supported-surface' },
						{ label: 'Known Limitations', slug: 'reference/known-limitations' },
					],
				},
				{
					label: 'Protocols',
					items: [
						{ label: 'Protocol Status', slug: 'protocols' },
						{ label: 'REST JSON', slug: 'protocols/simple-rest-json' },
						{ label: 'gRPC', slug: 'protocols/grpc' },
						{ label: 'RPC v2 CBOR', slug: 'protocols/rpc-v2-cbor' },
						{ label: 'REST XML', slug: 'protocols/rest-xml' },
						{ label: 'Conformance Tests', slug: 'protocols/conformance' },
					],
				},
				{
					label: 'Design',
					items: [
						{ label: 'Codegen Architecture', slug: 'design/codegen-architecture' },
						{ label: 'Shape Mapping', slug: 'design/shapes' },
						{ label: 'Serialization', slug: 'design/serialization' },
						{ label: 'HTTP Interfaces', slug: 'design/http-interfaces' },
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
