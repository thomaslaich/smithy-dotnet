// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import { readFileSync } from 'node:fs';

const smithyGrammar = JSON.parse(
	readFileSync(new URL('./src/smithy.tmLanguage.json', import.meta.url), 'utf-8')
);

// https://astro.build/config
export default defineConfig({
	site: 'https://thomaslaich.github.io',
	base: '/smithy-dotnet',
	integrations: [
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
						{ label: 'Quick Start', slug: 'quick-start' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'MSBuild', slug: 'msbuild' },
						{ label: 'Supported Surface', slug: 'reference/supported-surface' },
						{ label: 'Known Limitations', slug: 'reference/known-limitations' },
					],
				},
				{
					label: 'Guides',
					items: [
						{ label: 'Multi-Protocol', slug: 'multi-protocol' },
					],
				},
				{
					label: 'Protocols',
					items: [
						{ label: 'Protocol Status', slug: 'protocols' },
						{ label: 'Conformance Tests', slug: 'protocols/conformance' },
					],
				},
				{
					label: 'Architecture',
					items: [
						{ label: 'Hybrid Codegen', slug: 'architecture/hybrid-codegen' },
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
