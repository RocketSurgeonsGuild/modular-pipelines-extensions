# mise

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Commands](https://img.shields.io/badge/Commands-4-blue.svg)]()
[![Claude Code](https://img.shields.io/badge/Claude%20Code-Plugin-purple.svg)]()

User-global mise workflow commands: run release pipelines, check environment status, and discover tasks across any repo.

## Commands

| Command                  | Description                                                        | Trigger Keywords                                                                       |
| ------------------------ | ------------------------------------------------------------------ | -------------------------------------------------------------------------------------- |
| `/mise:run-full-release` | Run the current repo's mise release pipeline with auto-scaffolding | `mise release`, `full release`, `version bump`, `release automation`                   |
| `/mise:show-env-status`  | Show mise environment: tools, env vars, tasks, release readiness   | `mise status`, `mise env`, `repo status`, `environment check`                          |
| `/mise:list-repo-tasks`  | List mise tasks grouped by namespace with dependency info          | `mise tasks`, `task list`, `show tasks`, `available tasks`                             |
| `/mise:sred-commit`      | Create a git commit with SR&ED (CRA tax credit) trailers           | `sred commit`, `sred`, `cra commit`, `tax credit commit`, `scientific research commit` |

## Installation

```bash
/plugin marketplace add terrylica/cc-skills
/plugin install mise@cc-skills
```

## Usage

Commands are user-invoked via slash commands.

### Run a Release

```bash
# Full release pipeline
/mise:run-full-release

# Dry run (preview without changes)
/mise:run-full-release --dry

# Check release status
/mise:run-full-release --status
```

If the repo has no release tasks, the command audits the repo ecosystem (Python, Rust, Node) and scaffolds idiomatic `.mise/tasks/release/` tasks automatically.

### Check Environment Status

```bash
/mise:show-env-status
```

Shows installed tools, environment variables (non-sensitive), available tasks grouped by namespace, and release readiness.

### List Tasks

```bash
# All tasks, grouped by namespace
/mise:list-repo-tasks

# Filter to a specific namespace
/mise:list-repo-tasks release
```

### SR&ED Commits

```bash
/mise:sred-commit
```

Creates a conventional commit with SR&ED trailers (`SRED-Type`, `SRED-Claim`) for Canada CRA Scientific Research & Experimental Development tax credit compliance.

## What This Plugin Does NOT Do

This plugin **runs** existing mise tasks and reports on mise environments. It does not teach how to author mise configurations. For authoring patterns, see the `mise-tasks` and `mise-configuration` skills in the `itp` plugin.

## Dependencies

| Component        | Required | Installation                  |
| ---------------- | -------- | ----------------------------- |
| mise             | Yes      | `curl https://mise.run \| sh` |
| Git              | Yes      | 2.30+ (bundled)               |
| semantic-release | Optional | For release pipeline          |
| Bun              | Optional | For SR&ED commit validation   |

## License

MIT
