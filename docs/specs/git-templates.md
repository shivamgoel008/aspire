# Git-Based Template System for Aspire

**Status:** Draft
**Authors:** Aspire CLI Team
**Last Updated:** 2026-05-11

## 1. Overview

This spec defines a git-based templating system for the Aspire CLI's `aspire new` command. This is a new capability layered on top of the existing template infrastructure — it does not replace `dotnet new`. The `dotnet new` mechanism continues to serve the broader .NET developer ecosystem and reflects Aspire's heritage before the Aspire CLI existed. Both systems coexist: `dotnet new` for developers who prefer the standard .NET workflow, and `aspire new` for a richer, polyglot-friendly, community-oriented experience with git-hosted template content.

### Motivation: Templates Should Be Effortless

The best template ecosystems share a common trait: the distance between "I built something useful" and "anyone can use this as a starting point" is nearly zero. Today, creating an Aspire template requires packaging it as a NuGet package with `.template.config/template.json`, understanding the dotnet templating engine's symbol system, and publishing to a feed. This friction means that most useful Aspire applications never become templates, even when their authors would happily share them.

By making templates git repositories, we eliminate this friction entirely. An Aspire developer's natural workflow — build an app, push it to GitHub — becomes the template authoring workflow. Adding a single `aspire-template.json` file to the repo root is all it takes to make the project usable as a template by anyone in the world via `aspire new --template-repo`. To make it discoverable to other Aspire users in `aspire template list`, the author submits the repo to the Aspire template catalog at `aspire.dev`.

This has a compounding community effect:

- **Every public Aspire app is a potential template.** Developers who build interesting Aspire applications can share them with a single file addition. There's no separate "template authoring" skill to learn.
- **Polyglot templates are first-class citizens.** A TypeScript Aspire app and a C# Aspire app are both just directories of files. The same template system works for both without any language-specific plumbing.
- **The catalog is curated by aspire.dev.** Discovery, ranking, search, and moderation happen server-side. The CLI stays simple — it asks `aspire.dev` what's available and shows the answer.

### Design Principles

1. **Templates are real apps.** A template is a working Aspire AppHost project. Template authors develop, run, and test their templates as normal Aspire applications. The template engine personalizes the app via string substitution.
2. **Git-native distribution.** Templates are hosted in git repositories (GitHub initially). No NuGet packaging, no custom registries. If you can push to git, you can publish a template.
3. **Discovery is delegated.** The CLI does not crawl GitHub or walk template-index files. It asks a single `aspire.dev` HTTP endpoint "what templates exist for this query?" and renders the answer. Catalog management, search, ranking, and moderation are server-side concerns.
4. **Polyglot from day one.** Templates work for any language Aspire supports — C#, TypeScript, Python, Go, Java, Rust — because they're just real projects with variable substitution.
5. **Secure by design.** Templates are static file trees. No arbitrary code execution during template application. What you see in the repo is what you get.
6. **Zero-friction authoring.** Adding a single `aspire-template.json` file to any Aspire app repo makes it usable as a template via `--template-repo`. Submitting the repo URL to the aspire.dev catalog makes it discoverable in `aspire template list` for everyone.

### Goals

- Enable community-contributed templates without requiring access to the Aspire repo
- Support templates in any Aspire-supported language
- Keep the CLI simple and the server smart — discovery, search, and ranking live on `aspire.dev`
- Make template authoring as simple as "build an Aspire app, add a manifest"
- Maintain security guarantees — no supply chain risk from template application

### Non-Goals

- Deprecating or replacing `dotnet new` — that infrastructure serves the .NET ecosystem and will continue to exist alongside this system
- Building a federated, peer-to-peer template-index discovery system in the CLI (the previous design spike). All catalog logic is delegated to the `aspire.dev` service.
- Auto-discovering personal/org template repos via the GitHub CLI in the CLI. (The catalog service may surface organization-scoped views in the future, but the CLI does not call `gh`.)
- Building a template marketplace UI with ratings, reviews, or social features in the CLI (out of scope for v1; could happen on `aspire.dev` independently)
- Supporting non-git template hosting (e.g., OCI registries, NuGet packages) in the initial release
- Adding git-based template discovery to `dotnet new` — this is an Aspire CLI (`aspire new`) capability

## 2. Concepts

### Template

A **template** is a directory within a git repository that contains:

1. A working Aspire application (AppHost + service projects)
2. An `aspire-template.json` manifest describing the template's metadata, variables, and substitution rules

Because templates are real Aspire applications, template authors develop them using the normal Aspire workflow: `dotnet run`, `aspire run`, etc. The template engine's only job is to copy the files and apply string replacements to personalize the output.

### Template Service

The **template service** is an HTTP endpoint hosted on `aspire.dev` that returns a JSON document listing the templates available to the CLI. The CLI uses this single endpoint for discovery, search, and metadata. The service is responsible for:

- Maintaining the catalog of templates (which repositories are listed, at which commits or tags)
- Indexing templates for search across name, description, tags, and language
- Enforcing moderation policies (taking down abusive or compromised templates)
- Returning structured per-template metadata (display name, description, language, tags, repo URL, path within repo, ref, minimum Aspire version)

The CLI's job is to call the endpoint, cache the response, and render results. It is intentionally not aware of how the catalog is populated.

### Single-Template Repositories

A repository contains a single template if it has an `aspire-template.json` at its root. This is the most common case for community templates: a developer builds an Aspire app, adds `aspire-template.json` to the root, and their repo is immediately usable with `aspire new --template-repo`:

```text
my-cool-aspire-app/
├── aspire-template.json        # Makes this repo a template
├── MyCoolApp.sln
├── MyCoolApp.AppHost/
│   ├── Program.cs
│   └── MyCoolApp.AppHost.csproj
└── MyCoolApp.Web/
    └── ...
```

For multi-template repositories, individual templates live in subdirectories — each with its own `aspire-template.json` — and the catalog entry on `aspire.dev` points at the specific subdirectory via the `path` field (see §3).

### Template Source

A **template source** is a git repository (and optional path within it) that the CLI fetches template content from. Sources arrive in the CLI from one of three places:

| Priority | Source | How the CLI gets it |
|----------|--------|---------------------|
| 1 | Catalog | Returned by the `aspire.dev` template service endpoint (see §3) |
| 2 | Explicit URL | `aspire new --template-repo <url>` |
| 3 | Explicit local path | `aspire new --template-repo <local-path>` (for development) |

The catalog provides discovery; the explicit options bypass discovery for users who already know the repo URL or are developing/testing a template locally.

## 3. Schema: Template Service Response

The `aspire.dev` template service endpoint returns a JSON document listing templates. The CLI calls it via HTTP `GET` with optional query-string filters and parses the response.

### Endpoint

```text
GET https://aspire.dev/templates/index.json
GET https://aspire.dev/templates/index.json?q=<keyword>&language=<lang>
```

| Query parameter | Type | Description |
|-----------------|------|-------------|
| `q` | string | Free-text search across template names, descriptions, and tags. |
| `language` | string | Filter to templates whose primary language matches (e.g., `csharp`, `typescript`). |
| `limit` | integer | Maximum number of results to return. |
| `cursor` | string | Opaque cursor for pagination. |

The exact response shape is owned by the aspire.dev service; this section documents the contract the CLI relies on.

### Response Body

```json
{
  "$schema": "https://aka.ms/aspire/template-catalog-schema/v1",
  "version": 1,
  "generatedAt": "2026-05-11T12:00:00Z",
  "templates": [
    {
      "name": "aspire-starter",
      "displayName": "Aspire Starter Application",
      "description": "A full-featured Aspire starter with a web frontend and API backend.",
      "repo": "https://github.com/dotnet/aspire-templates",
      "path": "templates/aspire-starter",
      "ref": "v1.0.0",
      "language": "csharp",
      "tags": ["starter", "web", "api"],
      "publisher": {
        "name": "Microsoft",
        "url": "https://github.com/microsoft",
        "verified": true
      },
      "minAspireVersion": "9.0.0"
    },
    {
      "name": "aspire-ts-starter",
      "displayName": "Aspire TypeScript Starter",
      "description": "An Aspire application with a TypeScript AppHost.",
      "repo": "https://github.com/dotnet/aspire-templates",
      "path": "templates/aspire-ts-starter",
      "ref": "v1.0.0",
      "language": "typescript",
      "tags": ["starter", "typescript", "polyglot"],
      "publisher": {
        "name": "Microsoft",
        "url": "https://github.com/microsoft",
        "verified": true
      },
      "minAspireVersion": "9.2.0"
    },
    {
      "name": "contoso-microservices",
      "displayName": "Contoso Microservices Template",
      "description": "A production-grade microservices template maintained by the Contoso team.",
      "repo": "https://github.com/contoso/aspire-microservices-template",
      "path": ".",
      "ref": "main",
      "language": "csharp",
      "tags": ["microservices", "production", "partner"],
      "publisher": {
        "name": "Contoso",
        "url": "https://github.com/contoso",
        "verified": false
      }
    }
  ],
  "nextCursor": null
}
```

### Field Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `$schema` | string | No | JSON schema URL for validation and editor support |
| `version` | integer | Yes | Schema version. The CLI accepts `1` for this spec. |
| `generatedAt` | string (RFC 3339) | No | When the response was generated. Used for cache freshness indicators. |
| `templates` | array | Yes | List of template entries matching the query. |
| `templates[].name` | string | Yes | Unique machine-readable template identifier (kebab-case). |
| `templates[].displayName` | string | Yes | Human-readable template name. |
| `templates[].description` | string | Yes | Short description of the template. |
| `templates[].repo` | string | Yes | Git URL of the repository containing the template content. |
| `templates[].path` | string | Yes | Path to the template directory inside the repo, relative to the repo root. Use `"."` for repo-root templates. |
| `templates[].ref` | string | No | Git ref (branch, tag, or commit SHA) to fetch. If omitted, the CLI uses the repo's default branch. |
| `templates[].language` | string | No | Primary language of the template (`csharp`, `typescript`, `python`, `go`, `java`, `rust`). If omitted, the template is language-agnostic. |
| `templates[].tags` | array | No | Tags for filtering and categorization. |
| `templates[].publisher` | object | No | Information about the template publisher. |
| `templates[].publisher.name` | string | Yes (if publisher) | Display name of the publisher. |
| `templates[].publisher.url` | string | No | URL of the publisher. |
| `templates[].publisher.verified` | boolean | No | Whether this publisher has been verified by the catalog operator. |
| `templates[].minAspireVersion` | string | No | Minimum Aspire CLI version required to apply the template. |
| `nextCursor` | string \| null | No | Opaque cursor for fetching the next page of results. `null` when there are no more pages. |

The template service response intentionally mirrors the per-template metadata defined in §4 so that catalog responses stay self-contained and the CLI does not need to fetch each template's manifest just to render search results.

## 4. Schema: `aspire-template.json`

The template manifest lives inside a template directory (or at the root of a single-template repo) and describes how to apply the template. This is the source-of-truth manifest the template engine reads when scaffolding a new project.

```json
{
  "$schema": "https://aka.ms/aspire/template-schema/v1",
  "version": 1,
  "name": "aspire-starter",
  "displayName": "Aspire Starter Application",
  "description": "A full-featured Aspire starter with a web frontend and API backend.",
  "language": "csharp",
  "scope": ["new"],
  "variables": {
    "projectName": {
      "displayName": "Project Name",
      "description": "The name for your new Aspire application.",
      "type": "string",
      "required": true,
      "defaultValue": "AspireApp",
      "validation": {
        "pattern": "^[A-Za-z][A-Za-z0-9_.]*$",
        "message": "Project name must start with a letter and contain only letters, digits, dots, and underscores."
      }
    },
    "useRedisCache": {
      "displayName": "Include Redis Cache",
      "description": "Add a Redis cache resource to the AppHost.",
      "type": "boolean",
      "required": false,
      "defaultValue": false
    },
    "testFramework": {
      "displayName": "Test Framework",
      "description": "The test framework to use for the test project.",
      "type": "choice",
      "required": false,
      "choices": [
        { "value": "xunit", "displayName": "xUnit.net", "description": "xUnit.net test framework" },
        { "value": "nunit", "displayName": "NUnit", "description": "NUnit test framework" },
        { "value": "mstest", "displayName": "MSTest", "description": "MSTest test framework" }
      ],
      "defaultValue": "xunit"
    },
    "httpPort": {
      "displayName": "HTTP Port",
      "description": "The HTTP port for the web frontend.",
      "type": "integer",
      "required": false,
      "defaultValue": 5000,
      "validation": {
        "min": 1024,
        "max": 65535
      }
    }
  },
  "substitutions": {
    "filenames": {
      "AspireStarter": "{{projectName}}"
    },
    "content": {
      "AspireStarter": "{{projectName}}",
      "aspirestarter": "{{projectName | lowercase}}",
      "ASPIRE_STARTER": "{{projectName | uppercase}}"
    }
  },
  "conditionalFiles": {
    "tests/": "{{testFramework}}",
    "AspireStarter.AppHost/redis-config.json": "{{useRedisCache}}"
  },
  "postMessages": [
    "Your Aspire application '{{projectName}}' has been created!",
    "Run `cd {{projectName}} && dotnet run --project {{projectName}}.AppHost` to start the application."
  ],
  "postInstructions": [
    {
      "heading": "Get started",
      "priority": "primary",
      "lines": [
        "cd {{projectName}}",
        "dotnet run --project {{projectName}}.AppHost"
      ]
    },
    {
      "heading": "Redis setup",
      "priority": "secondary",
      "condition": "useRedisCache == true",
      "lines": [
        "Redis starts automatically via Aspire.",
        "To connect manually: docker run -d -p 6379:6379 redis"
      ]
    }
  ]
}
```

### Field Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `$schema` | string | No | JSON schema URL for validation and editor support |
| `version` | integer | Yes | Schema version. Must be `1` for this spec. |
| `name` | string | Yes | Machine-readable template identifier (must match the catalog entry if the template is published). |
| `displayName` | string \| object | Yes | Human-readable template name (see [Localization](#localization)) |
| `description` | string \| object | Yes | Short description (see [Localization](#localization)) |
| `language` | string | No | Primary language |
| `scope` | array | No | Where the template appears: `["new"]`, `["init"]`, or `["new", "init"]`. Default: `["new"]` |
| `variables` | object | Yes | Map of variable name → variable definition |
| `substitutions` | object | Yes | Substitution rules |
| `substitutions.filenames` | object | No | Map of filename patterns → replacement expressions |
| `substitutions.content` | object | No | Map of content patterns → replacement expressions |
| `conditionalFiles` | object | No | Files/directories conditionally included based on variable values |
| `postMessages` | array | No | Messages displayed to the user after template application |
| `postInstructions` | array | No | Structured instruction blocks shown after template application (see [Post-Instructions](#post-instructions)) |

### Variable Types

| Type | Description | Additional Properties |
|------|-------------|----------------------|
| `string` | Free-text string | `validation.pattern`, `validation.message` |
| `boolean` | True/false | — |
| `choice` | Selection from predefined options | `choices` array |
| `integer` | Numeric integer | `validation.min`, `validation.max` |

### Variable Field Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | Yes | Variable type: `string`, `boolean`, `choice`, `integer` |
| `displayName` | string \| object | No | Localizable prompt label |
| `description` | string \| object | No | Localizable help text |
| `required` | boolean | No | Whether a value must be provided |
| `defaultValue` | varies | No | Default value matching the variable type |
| `validation` | object | No | Validation rules (see Variable Types table) |
| `choices` | array | No | Available options for `choice` type variables |

### Substitution Expressions

Substitution values use a lightweight expression syntax:

| Expression | Description | Example Input | Example Output |
|------------|-------------|---------------|----------------|
| `{{variableName}}` | Direct substitution | `MyApp` | `MyApp` |
| `{{variableName \| lowercase}}` | Lowercase | `MyApp` | `myapp` |
| `{{variableName \| uppercase}}` | Uppercase | `MyApp` | `MYAPP` |
| `{{variableName \| kebabcase}}` | Kebab-case | `MyApp` | `my-app` |
| `{{variableName \| snakecase}}` | Snake_case | `MyApp` | `my_app` |
| `{{variableName \| camelcase}}` | camelCase | `MyApp` | `myApp` |
| `{{variableName \| pascalcase}}` | PascalCase | `myApp` | `MyApp` |

### Conditional Files

The `conditionalFiles` section controls which files are included in the output:

- **Boolean variables:** File is included only when the variable is `true`.
- **Choice variables:** File/directory is included only when the variable has a truthy (non-empty) value. For more granular control, use the naming convention `{{variableName}}-xunit/` where the directory name encodes the choice.

### Template Scope

The `scope` field controls where the template appears in the CLI:

| Scope Value | Description |
|-------------|-------------|
| `"new"` | Template appears in `aspire new` (creates a new project) |
| `"init"` | Template appears in `aspire init` (initializes an existing solution) |

The field is an array, so a template can appear in both contexts:

```json
"scope": ["new", "init"]
```

If omitted, scope defaults to `["new"]` for backward compatibility.

### Localization

String fields that are displayed to the user (`displayName`, `description`) support optional localization. Each such field accepts either a plain string or an object with culture-specific translations:

**Plain string (no localization):**

```json
"displayName": "Project Name"
```

**Localized string (culture keys):**

```json
"displayName": {
  "en": "Project Name",
  "de": "Projektname",
  "ja": "プロジェクト名"
}
```

The CLI resolves the best match using the current UI culture:

1. Try exact match (e.g., `en-US`)
2. Try parent culture (e.g., `en`)
3. Fall back to the first entry in the object

Localizable fields:

- `aspire-template.json`: `displayName`, `description`
- Variables: `displayName`, `description`
- Choices: `displayName`, `description`

Localization is optional — templates that use plain strings work unchanged. This design keeps templates self-contained in a single `aspire-template.json` file, avoiding the need for sidecar localization files (unlike the .NET template engine's `localize/templatestrings.{culture}.json` approach).

### Post-Instructions

The `postInstructions` field provides structured guidance shown to the developer after a template is applied. Unlike `postMessages` (plain strings), post-instructions support headings, priority levels, variable substitution, and conditional display.

#### Instruction Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `heading` | string \| object | Yes | Localizable heading displayed as a section title |
| `priority` | string | No | `"primary"` (highlighted with 🚀) or `"secondary"` (dimmed with ℹ️). Default: `"secondary"` |
| `lines` | array | Yes | Instruction lines. Support `{{variableName}}` substitution. |
| `condition` | string | No | Conditional expression controlling whether this block is shown |

#### Condition Syntax

| Syntax | Description | Example |
|--------|-------------|---------|
| `variable == value` | Equality (case-insensitive) | `"dbProvider == postgres"` |
| `variable != value` | Inequality (case-insensitive) | `"dbProvider != none"` |
| `variable` | Truthy check (non-empty and not `"false"`) | `"useRedisCache"` |

#### Rendering

Primary instructions are rendered first with bold formatting and a 🚀 prefix. Secondary instructions follow with dimmed formatting and an ℹ️ prefix. Variable placeholders (`{{name}}`) in lines are replaced with their values.

## 5. Template Directory Structure

A typical multi-template repository looks like this:

```text
aspire-templates/
├── README.md
├── templates/
│   ├── aspire-starter/
│   │   ├── aspire-template.json        # Template manifest
│   │   ├── AspireStarter.sln           # Working solution (template author can dotnet run this)
│   │   ├── AspireStarter.AppHost/
│   │   │   ├── Program.cs
│   │   │   └── AspireStarter.AppHost.csproj
│   │   ├── AspireStarter.Web/
│   │   │   ├── Program.cs
│   │   │   └── AspireStarter.Web.csproj
│   │   └── AspireStarter.ApiService/
│   │       ├── Program.cs
│   │       └── AspireStarter.ApiService.csproj
│   │
│   ├── aspire-ts-starter/
│   │   ├── aspire-template.json
│   │   ├── apphost.ts
│   │   ├── package.json
│   │   └── services/
│   │       └── api/
│   │           ├── index.ts
│   │           └── package.json
│   │
│   └── aspire-empty/
│       ├── aspire-template.json
│       ├── AspireEmpty.sln
│       └── AspireEmpty.AppHost/
│           ├── Program.cs
│           └── AspireEmpty.AppHost.csproj
```

A single-template repository is simpler — the manifest sits at the repo root and the catalog entry uses `"path": "."`:

```text
my-cool-aspire-app/
├── aspire-template.json
├── MyCoolApp.sln
└── MyCoolApp.AppHost/
    └── ...
```

### Key Insight: The Template IS the App

The `AspireStarter` directory is a fully functional Aspire application. The template author can:

```bash
cd templates/aspire-starter
dotnet run --project AspireStarter.AppHost
```

This runs the template as a real application. The template engine's job is simply to:

1. Copy the directory
2. Replace `AspireStarter` → `MyProjectName` in filenames and content
3. Exclude/include conditional files
4. Display post-creation messages

## 6. Template Resolution

When a user runs `aspire new` (or `aspire template list/search`), the CLI resolves available templates through a single delegated lookup against the `aspire.dev` template service.

### Resolution Flow

```text
                  ┌──────────────────────┐
                  │ Aspire CLI           │
                  │ (aspire new /        │
                  │  aspire template *)  │
                  └──────────┬───────────┘
                             │ HTTP GET (with optional query params)
                             ▼
              ┌──────────────────────────────┐
              │ aspire.dev/templates/        │
              │ index.json?q=<keyword>&...   │
              │                              │
              │ • Curates the catalog        │
              │ • Indexes for search         │
              │ • Returns JSON template list │
              └──────────┬───────────────────┘
                         │ JSON response
                         ▼
              ┌──────────────────────────────┐
              │ HTTP cache                   │
              │ (~/.aspire/templates-cache)  │
              │ TTL = templates.cacheTtlMin  │
              └──────────┬───────────────────┘
                         │
                         ▼
              ┌──────────────────────────────┐
              │ Render results / select      │
              │ a template entry             │
              └──────────┬───────────────────┘
                         │ {repo, path, ref}
                         ▼
              ┌──────────────────────────────┐
              │ git clone (shallow + sparse) │
              │ → ~/.aspire/templates-cache/ │
              │   repos/<hash>/<sha>/        │
              └──────────┬───────────────────┘
                         │ template directory on disk
                         ▼
              ┌──────────────────────────────┐
              │ Apply template (§7)          │
              └──────────────────────────────┘
```

### Phase 1: Catalog Lookup

1. Build the URL: start from `templates.serviceUrl` (default `https://aspire.dev/templates/index.json`) and append any query parameters required by the operation (`q=`, `language=`).
2. Check the local HTTP cache. If a cached response exists and is within the TTL (`templates.cacheTtlMinutes`, default `60`), return it.
3. Otherwise, perform a `GET` against the URL with the standard `aspire-cli/<version>` `User-Agent` header.
4. Parse the response (see §3). Validate `version == 1`.
5. Persist the response to the HTTP cache, keyed by full request URL.

### Phase 2: Template Selection

The user selects a template through one of:

- **Direct name (catalog):** `aspire new aspire-starter` → CLI calls the catalog endpoint, finds the entry with `name == "aspire-starter"`.
- **Explicit repo:** `aspire new --template-repo https://github.com/contoso/templates --template-name my-template` → CLI bypasses the catalog and fetches the repo directly.
- **Explicit local path:** `aspire new --template-repo ./my-template` → CLI uses the local directory directly. Useful for template authors during development.
- **Interactive:** `aspire new` → CLI lists catalog templates plus any built-in `dotnet new` templates and prompts the user to pick one.
- **Language filter:** `aspire new --language typescript` → CLI passes `?language=typescript` to the catalog and only TypeScript templates are shown.

### Phase 3: Template Fetch

Once a template is selected:

1. If the source is a remote repo, perform a shallow clone with sparse checkout targeting only `path` at the requested `ref`. Cache by `<repo-hash>/<resolved-commit-sha>` so concurrent runs of the same template hit the same on-disk content.
2. If the source is a local path, use it directly (no copy yet).
3. Read the `aspire-template.json` manifest from the template directory.
4. Prompt the user for any required variables (with defaults pre-filled).
5. Apply the template (see §7).

### Force Refresh

`aspire template refresh` (and `aspire new --refresh`) bypass the HTTP cache for the catalog response and force a fresh `GET`. Cached repository content keyed by commit SHA is left intact (it's already content-addressed), but stale entries are pruned according to the rules in §8.

## 7. Template Application

Template application is a deterministic, side-effect-free process:

```text
Input:  Template directory + variable values
Output: New project directory
```

### Algorithm

```text
1. COPY template directory → output directory
   - Skip: aspire-template.json (manifest is not part of output)
   - Skip: .git/, .github/ (git metadata from template repo)
   - Skip: files excluded by conditionalFiles rules

2. RENAME files and directories
   - For each entry in substitutions.filenames:
     Replace pattern with evaluated expression in all file/directory names
   - Process deepest paths first (to avoid renaming parent before child)

3. SUBSTITUTE content
   - For each file in output directory:
     For each entry in substitutions.content:
       Replace all occurrences of pattern with evaluated expression
   - Binary files are detected and skipped (by extension or content sniffing)

4. DISPLAY post-creation messages
   - Evaluate variable expressions in postMessages and postInstructions
   - Print to console
```

### Binary File Handling

The template engine skips content substitution for binary files. Binary detection uses:

1. **Extension allowlist:** `.png`, `.jpg`, `.gif`, `.ico`, `.woff`, `.woff2`, `.ttf`, `.eot`, `.pdf`, `.zip`, `.dll`, `.exe`, `.so`, `.dylib`
2. **Content sniffing fallback:** Check first 8KB for null bytes

### .gitignore Respect

Files matched by a `.gitignore` in the template directory are excluded from the output. This allows template authors to have local build artifacts that don't get copied.

## 8. Caching Strategy

The CLI maintains two caches: one for catalog responses (HTTP) and one for fetched template content (git). Both live under `~/.aspire/templates-cache/`.

### Catalog (HTTP) Cache

- **Location:** `~/.aspire/templates-cache/index/`
- **Scope:** One file per unique catalog request URL (so `?language=csharp` and `?q=redis` are cached independently).
- **TTL:** `templates.cacheTtlMinutes` (default: 60).
- **Force refresh:** `aspire template refresh` and `aspire new --refresh` bypass the cache and overwrite the entry.

### Repository (Git) Cache

- **Location:** `~/.aspire/templates-cache/repos/<repo-hash>/<commit-sha>/`
- **Strategy:** Templates are cached after first fetch. Cache is keyed by repo URL hash + resolved commit SHA, so two requests for the same content share storage.
- **Invalidation:** When the catalog returns a new commit SHA for the same template `name`, a fresh entry is created at the new SHA. Old entries are pruned by an LRU sweep when total cache size exceeds a configurable budget (TBD; tracked in §13).

### Cache Layout

```text
~/.aspire/
└── templates-cache/
    ├── index/
    │   ├── default.json                       # Cached aspire.dev/templates/index.json response
    │   ├── language=csharp.json               # Cached language filter response
    │   ├── q=redis.json                       # Cached search response
    │   └── cache-metadata.json                # TTL tracking, last-fetched timestamps
    └── repos/
        ├── a1b2c3d4/                          # Hash of repo URL
        │   ├── 9f8e7d6c5b4a3210/              # Resolved commit SHA
        │   │   └── templates/aspire-starter/
        │   └── 1234567890abcdef/
        │       └── templates/aspire-starter/
        └── e5f6g7h8/
            └── deadbeef00112233/
                └── .
```

## 9. CLI Integration

### Feature Flag

The entire git-based template system is gated behind a feature flag:

```text
features.gitTemplatesEnabled = true|false (default: false)
```

When disabled, the `aspire template` command tree is not registered on the root command and the catalog-backed template factory contributes no templates to `aspire new`. This allows incremental development without affecting existing users.

Enable for local testing:

```bash
aspire config set features.gitTemplatesEnabled true
```

### Configuration Values

Configuration is intentionally minimal. Only two keys control git-based template behavior:

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `features.gitTemplatesEnabled` | bool | `false` | Enable/disable the git-based template system. |
| `templates.serviceUrl` | string | `https://aspire.dev/templates/index.json` | URL of the template catalog endpoint. Override for testing or air-gapped deployments. |
| `templates.cacheTtlMinutes` | int | `60` | Cache TTL for the catalog HTTP response in minutes. |

```bash
# Point at a non-production catalog endpoint (e.g., for testing the service)
aspire config set -g templates.serviceUrl https://staging.aspire.dev/templates/index.json

# Reduce cache TTL for development
aspire config set -g templates.cacheTtlMinutes 5
```

There is intentionally no key for personal/org template auto-discovery, no per-source TTL, no list of "additional indexes", no `defaultBranch`, and no `enablePersonalDiscovery`/`enableOrgDiscovery` switches. Whatever discovery features we offer are surfaced by the catalog service itself.

### Command Tree: `aspire template`

The `aspire template` command group provides template management for end users. All subcommands are gated on the `gitTemplatesEnabled` feature flag.

```text
aspire template
├── list              List available templates from the catalog
├── search <keyword>  Search templates by name, description, or tags
├── refresh           Force refresh the catalog cache
└── new [path]        Scaffold a new aspire-template.json manifest
```

Notably, there is no `aspire template new-index` command. End users do not author template indexes — the catalog is owned by the `aspire.dev` service.

#### `aspire template list`

Calls the catalog endpoint and lists all returned templates:

```text
$ aspire template list

  Name                     Language    Description
  aspire-starter           C#          Full-featured Aspire starter application
  aspire-ts-starter        TypeScript  Aspire with TypeScript AppHost
  aspire-py-starter        Python      Aspire with Python AppHost
  aspire-azure-functions   C#          Aspire with Azure Functions
  contoso-microservices    C#          Microservices pattern with Aspire

Options:
  --language <lang>        Filter by language (csharp, typescript, python, etc.)
  --json                   Output as JSON (for automation/scripting)
```

#### `aspire template search <keyword>`

Calls the catalog endpoint with `?q=<keyword>` and lists the results:

```text
$ aspire template search redis

Results for "redis":
  Name                     Language    Description
  aspire-redis-starter     C#          Aspire starter with Redis cache
  redis-microservices      C#          Microservices pattern with Redis

Options:
  --language <lang>        Filter by language
  --json                   Output as JSON
```

#### `aspire template refresh`

Bypasses the HTTP cache and re-fetches the catalog:

```text
$ aspire template refresh

Refreshed catalog from https://aspire.dev/templates/index.json
17 templates available.
```

#### `aspire template new [path]`

Scaffolds a new `aspire-template.json` manifest file. This helps template authors get started:

```text
$ aspire template new

Creating aspire-template.json...

? Template name (kebab-case): my-cool-template
? Display name: My Cool Template
? Description: A template for building cool things with Aspire
? Primary language: csharp
? Canonical project name to replace: MyCoolTemplate

Created aspire-template.json with substitution rules for "MyCoolTemplate".
Next steps:
  1. Review and customize aspire-template.json
  2. Test with: aspire new --template-repo . --name TestApp
  3. Push to git and (optionally) submit to the aspire.dev catalog
```

If `[path]` is provided, creates the manifest at that path instead of the current directory.

### Modified Commands

#### `aspire new`

When `gitTemplatesEnabled` is `true`, `aspire new` shows templates from both the existing `DotNetTemplateFactory` and the new catalog-backed git template factory. Catalog-sourced templates appear alongside built-in templates:

```text
aspire new [template-name] [options]

Arguments:
  template-name    Name of the template to use (optional, interactive if omitted)

Options:
  -n, --name <name>              Project name
  -o, --output <path>            Output directory
  --language <lang>              Filter templates by language
  --template-repo <url|path>     Use a git template from a specific repo or local path (bypasses catalog)
  --template-name <name>         Template name within the specified repo
  --refresh                      Bypass the catalog HTTP cache
```

When `--template-repo` is used, the CLI fetches the specified repo or reads the local path, looks for `aspire-template.json` (or, for multi-template repos, `<path>/aspire-template.json`), and applies the template directly — bypassing the catalog entirely.

##### CLI Variable Binding

Git template variables can be provided on the command line as `--variableName value` pairs, allowing non-interactive template application. Variables not provided on the CLI are prompted interactively as usual.

```text
# Fully interactive (all variables prompted)
aspire new my-template

# Partially non-interactive (provided values skip prompts)
aspire new my-template --useRedis true --dbProvider postgres

# Fully non-interactive (all variables provided)
aspire new my-template --name MyProject --useRedis true --dbProvider postgres --port 5432
```

**Naming**: Both camelCase (`--useRedis`) and kebab-case (`--use-redis`) are accepted and matched against manifest variable names.

**Boolean variables**: A bare flag (`--useRedis`) is treated as `true`. Explicit values (`--useRedis false`) are also supported.

**Choice variables**: The raw `value` from the choice definition is used on the CLI (e.g., `--dbProvider postgres`), not the localized `displayName`. Invalid values produce an error listing the valid choices:

```text
Error: Invalid value 'pg' for variable 'dbProvider'. Valid choices are: postgres, sqlserver, mysql
```

**Validation**: CLI-provided values go through the same validation rules (regex patterns, min/max bounds, choice membership) as interactively prompted values.

## 10. Security Model

### Threat Model

| Threat | Mitigation |
|--------|-----------|
| Malicious code in template files | Templates are static files. No code execution during application. Users can inspect the template repo before using it. |
| Supply chain attack via catalog poisoning | Catalog is hosted on `aspire.dev` with operational ownership by the Aspire team. Submissions are reviewed and the publisher's `verified` flag distinguishes first-party / verified-partner entries from unverified community entries. |
| Compromised template repo | Catalog entries can be pinned to a specific `ref` (tag or commit SHA). The aspire.dev service can update or revoke entries; the CLI honors the latest catalog response after the cache TTL. |
| Man-in-the-middle on catalog fetch | Catalog HTTPS only. The CLI rejects non-HTTPS service URLs unless they are an explicit `localhost` / `127.0.0.1` (development override). |
| Man-in-the-middle on template fetch | Git clone uses HTTPS. Cache entries are keyed by resolved commit SHA, providing integrity verification. |
| Typosquatting template names | The catalog service is responsible for namespace policy. The CLI surfaces the `verified` flag and the `repo` URL in interactive selection so users can sanity-check before applying. |
| Malicious post-generation hooks | No hooks. Templates do not support arbitrary code execution. |
| Sensitive data in templates | Templates are public git repos. No secrets should be in templates. `.gitignore` is respected. |

### Trust Levels

Templates are categorized by trust based on the catalog metadata:

| Level | Source | UX Treatment |
|-------|--------|-------------|
| **Verified** | Catalog entries with `publisher.verified == true` | No warnings, shown first in interactive selection |
| **Catalog (community)** | Catalog entries without verification | Subtle note about source; repo URL surfaced |
| **Explicit URL** | User-supplied `--template-repo <url>` | Confirmation prompt on first use of an unfamiliar URL |
| **Local path** | User-supplied `--template-repo <local-path>` | No prompt — the user is the source of trust |

### What Templates Cannot Do

This is a critical security property. Template application is purely:

- File copy
- String substitution in filenames and content
- Conditional file inclusion/exclusion

Templates **cannot**:

- Execute arbitrary commands
- Run scripts (pre or post generation)
- Access the network
- Read files outside the template directory
- Modify the user's system configuration
- Install packages or dependencies

If a template needs post-creation setup (e.g., `npm install`, `dotnet restore`), the `postMessages` and `postInstructions` fields can instruct the user, but the CLI does not execute these automatically.

### Integrity & Content Verification

> **TODO: This section requires input from the security team before finalizing.**

Template content is fetched from git repositories over the network, which introduces integrity concerns beyond the basic threat model above. The following areas need security review:

#### Catalog Integrity

- **Question:** Should the CLI verify a signature on the catalog response (e.g., a JWS / detached signature served alongside the JSON)? If the catalog service is compromised, every CLI install becomes vulnerable.
- **Possible approach:** The catalog response could include or be accompanied by a signature signed with a key embedded in the CLI; the CLI verifies before trusting any entry.

#### Template Content Integrity

- **Question:** Should we compute and verify checksums of template files after fetch?
- **Possible approach:** Cache entries are already keyed by repo URL + resolved commit SHA, providing basic integrity. Catalog entries that pin a `ref` to a tag (rather than a branch) further reduce drift; pinning to a SHA eliminates it.

#### Cache Poisoning

- **Question:** If an attacker can write to the local cache directory (`~/.aspire/templates-cache/`), they could substitute malicious content. Should we sign cache entries?
- **Possible approach:** Cache entries could include a manifest with commit SHAs and checksums, verified on read.

#### Audit Trail

- **Question:** Should the CLI log which template was used, from which catalog entry, at which commit SHA, when a project is created? This would help with incident response if a template source is later found to be compromised.
- **Possible approach:** Write a `template-provenance.json` to the generated project recording catalog entry name, repo URL, resolved commit SHA, template version, and timestamp.

## 11. Polyglot Support

Because templates are real Aspire applications, polyglot support is inherent:

### C# Template Example

```text
templates/aspire-starter/
├── aspire-template.json
├── AspireStarter.sln
├── AspireStarter.AppHost/
│   ├── Program.cs
│   └── AspireStarter.AppHost.csproj
└── AspireStarter.Web/
    ├── Program.cs
    └── AspireStarter.Web.csproj
```

### TypeScript Template Example

```text
templates/aspire-ts-starter/
├── aspire-template.json
├── apphost.ts
├── package.json
├── tsconfig.json
└── services/
    └── api/
        ├── index.ts
        └── package.json
```

### Python Template Example

```text
templates/aspire-py-starter/
├── aspire-template.json
├── apphost.py
├── requirements.txt
└── services/
    └── api/
        ├── app.py
        └── requirements.txt
```

The template engine doesn't need to know anything about the language. It operates purely on files and strings.

## 12. Template Authoring Guide

Creating an Aspire template is designed to be trivially easy. Here's the complete workflow:

### The 5-Minute Path: Your Repo IS the Template

If you have a working Aspire application in a git repo, you're 90% of the way to a template:

**Step 1:** Pick a canonical project name. This is the string that will be replaced with the user's project name. For example, if your project is called `ContosoShop`, that's your canonical name.

**Step 2:** Create `aspire-template.json` in the repo root:

```json
{
  "$schema": "https://aka.ms/aspire/template-schema/v1",
  "version": 1,
  "name": "contoso-shop",
  "displayName": "Contoso Shop",
  "description": "A microservices e-commerce application with Aspire.",
  "language": "csharp",
  "variables": {
    "projectName": {
      "displayName": "Project Name",
      "description": "The name for your new application.",
      "type": "string",
      "required": true,
      "defaultValue": "ContosoShop"
    }
  },
  "substitutions": {
    "filenames": {
      "ContosoShop": "{{projectName}}"
    },
    "content": {
      "ContosoShop": "{{projectName}}"
    }
  }
}
```

**Step 3:** Push to GitHub. That's it.

Anyone can now use your template:

```bash
aspire new --template-repo https://github.com/you/contoso-shop --name MyShop
```

### Making It Discoverable

To make your template show up in `aspire template list` and the interactive selector for `aspire new` (without users needing the `--template-repo` URL), submit your repo to the aspire.dev catalog. The submission process and SLA are owned by the catalog service team and will be documented separately at `aspire.dev/templates/contributing` (TBD). At a minimum, expect to provide:

- The repo URL and (optional) `ref` you want pinned
- The path within the repo if it's a multi-template repo
- Publisher metadata (name, URL)

The catalog operator reviews the submission for basic quality (manifest validity, the template builds, no obvious malicious content) before listing it.

### Multi-Template Repositories

If you maintain multiple templates in one repo, just put each template in its own directory with its own `aspire-template.json`. The catalog entries for each template point at the right subdirectory via `path`.

```text
my-templates/
├── README.md
└── templates/
    ├── basic-api/
    │   ├── aspire-template.json
    │   └── ...
    └── full-stack/
        ├── aspire-template.json
        └── ...
```

There is no per-repo `aspire-template-index.json` file. The aspire.dev catalog is the source of truth for which templates exist, where they live, and how they relate to one another.

### Testing Your Template

Since your template is a working Aspire app, you test it by running it:

```bash
# Test the template as an app
cd templates/basic-api
dotnet run --project BasicApi.AppHost

# Test the template engine
aspire new --template-repo . --name TestOutput -o /tmp/test-output
cd /tmp/test-output
dotnet run --project TestOutput.AppHost
```

Both commands should work. The first verifies your app works, the second verifies the substitutions produce a working app.

## 13. Open Questions

These items need further discussion before finalizing:

1. **Catalog response shape:** The schema in §3 is the CLI's working assumption. The aspire.dev service team owns the canonical contract — fields may evolve. The CLI must keep the response model versioned and tolerate unknown fields.

2. **Catalog submission/governance:** Submission flow, review SLAs, takedown policy, and verified-publisher criteria are owned by the catalog service and out of scope for this CLI spec — but the CLI's UX (e.g. trust-level badges) depends on the publisher metadata being meaningful.

3. **Catalog signing:** Should the CLI verify a signature on the catalog response to guard against a compromised endpoint? See §10.

4. **Cache size budget:** What's the right LRU eviction threshold for the repository cache (`~/.aspire/templates-cache/repos/`) — 500 MB? 1 GB? Configurable?

5. **Offline story:** What happens when the user has no network access and no cached catalog? Should we ship a minimal set of embedded templates that always work?

6. **Version pinning:** Should a user be able to pin to a specific version of a template (e.g., "give me `aspire-starter` at the version that shipped with Aspire 9.2")? The catalog already exposes `ref`, but the UX for "pick a specific version" is undefined.

7. **Template inheritance/composition:** Should templates be able to extend or compose other templates? (e.g., "start with aspire-starter, add Azure Service Bus")

8. **Template validation:** Should we provide an `aspire template validate` command for template authors? What validation rules?

9. **Private repos:** Should we support templates from private git repos via `--template-repo`? What authentication flows?

10. **Template updates:** When a user has an existing project created from a template, should we support updating/diffing against newer template versions?

## 14. Future Considerations

These are explicitly out of scope for v1 but worth tracking:

- **Template marketplace UI on aspire.dev:** A web UI for discovering and previewing templates (independent of the CLI).
- **Template testing framework:** Automated testing for template authors to verify their templates work — could ship as `aspire template test` in a polish phase.
- **IDE integration:** VS/VS Code extensions that surface catalog templates in the new-project dialog.
- **Template analytics:** Opt-in usage tracking to help template authors understand adoption.
- **OCI registry support:** Distributing templates as OCI artifacts for air-gapped environments.
- **Template generators:** Executable templates for advanced scenarios (with appropriate security guardrails).

## 15. Implementation Plan

This section outlines the incremental implementation strategy. The approach is command-first: stub out the `aspire template` command tree early, then use those commands as the primary interface for developing and testing the underlying infrastructure. Each phase is a small, mergeable PR.

### Phase 1: Foundation — Command Tree & Feature Flag

**Goal:** Get `aspire template *` commands visible and responding (with stub output) behind a feature flag. No real catalog or template content yet.

**Work items:**

1. Add this spec doc at `docs/specs/git-templates.md`.
2. Add feature flag `gitTemplatesEnabled` to `KnownFeatures.cs` (default: `false`).
3. Create the `aspire template` command group with stub subcommands:
   - `aspire template list`
   - `aspire template search <keyword>`
   - `aspire template refresh`
   - `aspire template new [path]`
4. Wire the parent command into `RootCommand` under the feature flag.
5. Register all commands in DI in `Program.cs` and `CliTestHelper.cs`.
6. Basic command-parse and feature-flag-gating tests.

**Key files:**

```text
src/Aspire.Cli/
├── Commands/
│   └── Template/
│       ├── GitTemplateCommand.cs              # Parent: aspire template
│       ├── GitTemplateListCommand.cs          # aspire template list
│       ├── GitTemplateSearchCommand.cs        # aspire template search <keyword>
│       ├── GitTemplateRefreshCommand.cs       # aspire template refresh
│       └── GitTemplateNewCommand.cs           # aspire template new [path]
└── KnownFeatures.cs                           # + gitTemplatesEnabled flag
```

The C# class names use a `Git` prefix (`GitTemplateCommand`, etc.) to avoid type-name collision with the existing `Aspire.Cli.Commands.TemplateCommand` (the per-template wrapper used by `NewCommand`). The user-facing CLI verb is still `aspire template`.

### Phase 2: Schema, Models, & `template new` Scaffolding

**Goal:** `aspire template new` produces a valid, well-formed `aspire-template.json` manifest interactively.

**Work items:**

1. Define C# models for the `aspire-template.json` schema:
   - `GitTemplateManifest` — top-level manifest
   - `GitTemplateVariable`, `GitTemplateSubstitutions`, `GitTemplateChoice`, etc.
2. Define the catalog-response model (matching §3).
3. Implement `aspire template new` — interactive prompts (via `IInteractionService`) to collect template name, canonical project name, language, and write `aspire-template.json` with sensible substitution defaults.
4. Publish JSON schemas at `https://aka.ms/aspire/template-schema/v1` and `https://aka.ms/aspire/template-catalog-schema/v1` (or embed for offline use).

### Phase 3: Catalog Service Client & `list` / `search` / `refresh`

**Goal:** `aspire template list / search / refresh` return real data from a service endpoint.

**Work items:**

1. `IGitTemplateCatalogClient` — service that fetches the catalog response over HTTP, validates it, and caches it.
2. HTTP cache layer with TTL keyed by request URL.
3. `aspire template refresh` invalidates the cache and re-fetches.
4. Render the results via `IInteractionService.DisplayRenderable()`.
5. **Mock catalog for development & tests:** ship a baked-in catalog JSON file (matching the §3 schema) that the client falls back to when `templates.serviceUrl` points at it (e.g., `dev:embedded` URI). Tests use this exclusively. Real `aspire.dev` integration follows once the service contract is finalized with the service team.

### Phase 4: Template Application Engine

**Goal:** `aspire new --template-repo <url|path>` creates a real project from a git-based template.

**Work items:**

1. `IGitTemplateEngine` — service that applies a template:
   - Clone template content (shallow + sparse checkout of the template path at the requested ref)
   - Read `aspire-template.json` manifest
   - Prompt for variables
   - Copy files with exclusions (manifest, `.git/`, `.github/`, conditional files)
   - Apply filename substitutions (deepest-first)
   - Apply content substitutions (skip binary files)
   - Display post-creation messages and post-instructions
2. Variable expression evaluator — handles `{{var}}`, `{{var | lowercase}}`, etc.
3. Binary file detection — extension allowlist + null-byte sniffing.
4. Implement `--template-repo` and `--template-name` flags on `aspire new`.

### Phase 5: Catalog-Backed Template Factory in `aspire new`

**Goal:** Catalog templates appear in `aspire new` interactive selection alongside built-in `dotnet new` templates.

**Work items:**

1. `GitTemplateFactory : ITemplateFactory` — returns `ITemplate` instances backed by catalog entries.
   - Uses `IGitTemplateCatalogClient` for discovery.
   - Each template is a `GitTemplate : ITemplate` that delegates to `IGitTemplateEngine`.
2. Register in `Program.cs` via `TryAddEnumerable` (same pattern as `DotNetTemplateFactory`).
3. Template deduplication — when both factories provide a template with the same name, prefer the catalog entry if it's `verified`, else `dotnet new`.
4. Interactive selection — `aspire new` without arguments shows all templates grouped by source.

### Phase 6: Polish & GA

**Goal:** Production-ready for public use.

**Work items:**

1. Error handling — graceful degradation when network is unavailable or the catalog is unreachable.
2. Progress indicators — show clone/fetch progress via `IInteractionService`.
3. Telemetry — template usage events (template name, source, language — no PII).
4. User-facing documentation for template authoring and the `aspire template` command group.
5. Flip the `gitTemplatesEnabled` default to `true`.
6. Stand up the production `aspire.dev/templates/index.json` endpoint with a curated set of official templates.

## Appendix A: Research & Prior Art

### .NET Template Engine (`dotnet new`)

The .NET template engine already embraces the "runnable project" philosophy. From the [template.json reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json):

> A "runnable project" is a project that can be executed as a normal project can. Instead of updating your source files to be tokenized you define replacements, and other processing, mostly in an external file, the `template.json` file.

Our git-based system builds on this proven concept. `dotnet new` continues to serve the .NET ecosystem with NuGet-packaged templates and is not replaced or deprecated by this spec. `aspire new` adds git-based distribution as a complementary layer for Aspire CLI users.

**Comparison:**

| Aspect | `dotnet new` Templates | Aspire Git Templates (`aspire new`) |
|--------|----------------------|---------------------|
| **Distribution** | NuGet packages | Git repositories |
| **Discovery** | NuGet feeds, `dotnet new search` | Service-mediated catalog at `aspire.dev` |
| **Manifest** | `.template.config/template.json` | `aspire-template.json` (template root) |
| **Substitution** | Symbol-based with generators, computed symbols, conditional preprocessing | Simple variable substitution with filters |
| **Post-actions** | Executable (restore, open IDE, run script) | Messages and instructions only (no code execution) |
| **Language scope** | .NET languages | Any language |
| **Authoring** | Create template, package as NuGet, publish to feed | Add JSON file to repo, push to git, (optionally) submit to catalog |
| **GUID handling** | Automatic GUID regeneration across formats | Not yet specified (see Open Questions) |
| **Coexistence** | Continues as-is | Additive — does not replace `dotnet new` |

**Design rationale for divergence:**

We intentionally keep the substitution model simpler than `dotnet new`'s full symbol/generator system. The .NET template engine supports computed symbols, derived values, and conditional preprocessing — powerful features but ones that add complexity and .NET-specific concepts. Our system favors simplicity and transparency: what you see in the repo is what you get, with straightforward name replacements. If complex project generation is needed, build a tool that generates the output directly.

### Cookiecutter

[Cookiecutter](https://cookiecutter.readthedocs.io/) is a popular cross-language project templating tool. Templates are directories with `{{cookiecutter.variable}}` placeholders in filenames and content, plus a `cookiecutter.json` defining variables and defaults.

**What we borrow:**

- The concept that a template is a directory of real files with variable placeholders
- JSON manifest for variable definitions with defaults
- Git repository as distribution mechanism (`cookiecutter gh:user/repo`)

**Where we differ:**

- Cookiecutter uses Jinja2 templating (powerful but complex); we use simple find-and-replace with filters
- Cookiecutter supports pre/post-generation hooks (Python/shell scripts); we explicitly forbid code execution for security
- Cookiecutter has no centralized discovery story; we delegate discovery to `aspire.dev`

### GitHub Template Repositories

GitHub's [template repositories](https://docs.github.com/en/repositories/creating-and-managing-repositories/creating-a-template-repository) allow creating new repos from a template with one click. They're simple (just copy files) but have no variable substitution, no parameterization, and no CLI integration.

**What we borrow:**

- The idea that a git repo IS the template
- Zero-friction publishing (just mark a repo as a template)

**Where we differ:**

- We add variable substitution for project name personalization
- We add catalog-mediated discovery via `aspire.dev`
- We integrate with the Aspire CLI rather than GitHub's web UI

### Yeoman

[Yeoman](https://yeoman.io/) generators are npm packages that programmatically scaffold projects. They're extremely flexible but require JavaScript knowledge to author and npm infrastructure to distribute.

**What we learn:**

- Yeoman's power comes at the cost of high authoring complexity — most teams never create generators
- The npm distribution model creates friction for non-JavaScript ecosystems
- Our approach inverts this: minimal authoring effort, git-native distribution

### Rollout Plan

The git-based template system will be introduced alongside `dotnet new`:

1. **Phase 1 (this PR):** Spec doc, feature flag, and stub command group. No real catalog or template content yet.
2. **Subsequent phases (see §15):** Schema models, catalog client, template engine, factory integration, polish.
3. **GA:** Flip the feature flag default to `true` once the production catalog is live and a baseline set of official templates is published.

### For `dotnet new` Template Authors

Teams currently creating `dotnet new` templates can additionally make them available as git-based templates:

1. Take your existing template output (what `dotnet new` generates)
2. Replace parameter placeholders with the canonical project name
3. Add `aspire-template.json` with variable definitions
4. Push to a git repo
5. Optionally, submit to the aspire.dev catalog

This is additive — the `dotnet new` template continues to work as before.
