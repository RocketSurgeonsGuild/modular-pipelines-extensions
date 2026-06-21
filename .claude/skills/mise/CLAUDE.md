# mise Plugin

> User-global mise workflow commands — run release pipelines, check environment status, and discover tasks across any repo.

**Hub**: [Root CLAUDE.md](../../CLAUDE.md) | **Sibling**: [itp CLAUDE.md](../itp/CLAUDE.md)

## Commands

| Command                  | Purpose                                                          | Flags                      |
| ------------------------ | ---------------------------------------------------------------- | -------------------------- |
| `/mise:run-full-release` | Run the current repo's mise release pipeline                     | `--dry`, `--status`        |
| `/mise:show-env-status`  | Show mise environment: tools, env vars, tasks, release readiness | —                          |
| `/mise:list-repo-tasks`  | List mise tasks grouped by namespace with dependencies           | `[namespace]`              |
| `/mise:sred-commit`      | Create a git commit with SR&ED (CRA tax credit) trailers         | `[commit message summary]` |

## Command Naming Convention

All mise plugin commands use **3-4 hyphenated words** for maximum readability:

| Pattern                 | Example            | Structure               |
| ----------------------- | ------------------ | ----------------------- |
| `verb-qualifier-object` | `run-full-release` | action + scope + target |
| `verb-object-context`   | `show-env-status`  | action + what + domain  |
| `verb-scope-object`     | `list-repo-tasks`  | action + where + what   |

## Release Bootstrapping

`/mise:run-full-release` serves dual purpose: runs existing release pipelines, or guides bootstrapping one for new repos. When no release tasks exist, it audits the repo's ecosystem and scaffolds an individualized 5-phase pipeline (preflight → version → publish → verify → postflight).

Reference: cc-skills' own `.mise/tasks/release/` and `docs/RELEASE.md`.

## Related Skills

- `mise-tasks` skill (in itp) — task orchestration with `[tasks]`, dependencies, DAGs
- `mise-configuration` skill (in itp) — environment SSoT with `[env]`, `[tools]`, templates

## Skills

- [list-repo-tasks](./skills/list-repo-tasks/SKILL.md)
- [run-full-release](./skills/run-full-release/SKILL.md)
- [show-env-status](./skills/show-env-status/SKILL.md)
- [sred-commit](./skills/sred-commit/SKILL.md)
