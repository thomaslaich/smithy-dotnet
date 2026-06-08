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
						{ label: 'Endpoint Documentation', slug: 'guides/endpoint-documentation' },
						{ label: 'Distributing Contracts', slug: 'guides/distributing-contracts' },
					],
				},
				{
					label: 'Protocols',
					items: [
						{ label: 'Overview', slug: 'protocols/overview' },
						{ label: 'REST JSON', slug: 'protocols/rest-json' },
						{ label: 'gRPC', slug: 'protocols/grpc' },
						{
							label: 'AWS Protocols',
							items: [
								{ label: 'Overview', slug: 'protocols/aws-overview' },
								{ label: 'REST XML', slug: 'protocols/rest-xml' },
								{ label: 'RPC v2 CBOR', slug: 'protocols/rpc-v2-cbor' },
							],
						},
						{ label: 'Conformance Tests', slug: 'protocols/conformance' },
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
