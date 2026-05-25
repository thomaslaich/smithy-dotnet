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
						{ label: 'Environment Setup', slug: 'getting-started/environment' },
						{ label: 'Contracts Project', slug: 'guides/contracts-project' },
						{ label: 'Distributing Contracts', slug: 'guides/distributing-contracts' },
						{ label: 'Multi-Protocol', slug: 'guides/multi-protocol' },
					],
				},
				{
					label: 'Protocols',
					items: [
						{ label: 'Protocol Status', slug: 'protocols' },
						{ label: 'REST JSON', slug: 'protocols/rest-json' },
						{ label: 'gRPC', slug: 'protocols/grpc' },
						{
							label: 'AWS Protocols',
							items: [
								{ label: 'REST XML', slug: 'protocols/rest-xml' },
								{ label: 'RPC v2 CBOR', slug: 'protocols/rpc-v2-cbor' },
							],
						},
						{ label: 'Conformance Tests', slug: 'protocols/conformance' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'MSBuild', slug: 'reference/msbuild' },
						{ label: 'Known Limitations', slug: 'reference/known-limitations' },
					],
				},
				{
					label: 'Design',
					items: [
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
