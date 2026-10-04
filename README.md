# backyard

A tiny terminal farm that grows a little every time Claude Code finishes a task.

No network access, no background process. It stores only event kinds and timestamps.

It comes in two forms: a one-line summary for your statusline, and a full-screen TUI you open when you want to tend the farm.

**The statusline** (`backyard status`):

```text
[. , v Y _ _ _ _ _ _] │ 12G │ ~ 4m
```

This line sits in your Claude Code statusline and updates as the farm grows.

**The farm window** (`backyard`):

<img width="1920" height="1080" alt="Image" src="https://github.com/user-attachments/assets/8c8fea2b-4966-4bbd-8b80-722cd976740b" />

Opens a separate TUI with the garden, shop and codex. Here you harvest, sell, plant better seeds, or buy another row.

<br>

## Installing

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download)

```sh
dotnet tool install -g backyard-farm
backyard install
```

`backyard install` adds `backyard status` to the statusline in `~/.claude/settings.json` (appending to any existing statusline command).

Start a new Claude Code session and the farm appears.

<br>

## Usage

| Command              | What it does                                                                  |
| -------------------- | ----------------------------------------------------------------------------- |
| `backyard install`   | Registers `backyard status` in the statusline.                                |
| `backyard status`    | Prints the one-line summary used by the statusline.                           |
| `backyard`           | Opens the TUI: garden, shop, codex.                                           |
| `backyard doctor`    | Shows why tasks are not being detected: paths, last event, parse errors.      |
| `backyard reset`     | Deletes the farm and starts over.                                             |
| `backyard uninstall` | Removes the statusline entry, the state directory and the `backyard://` link. |

Update with `dotnet tool update -g backyard-farm`.

Remove with `backyard uninstall` followed by `dotnet tool uninstall -g backyard-farm`.

<br>

## How the farm grows

- Finishing a Claude Code task that used a tool waters every dry cell.
- A cell that is already watered ignores further tasks until it grows.
- Ripe crops are harvested and sold in the TUI.
- No usage-based rewards, no streaks, no wilting.

<br>

## Privacy

backyard reads the Claude Code transcripts under `~/.claude/projects` to find "task finished" events. It keeps only the event kind, the timestamp, and a per-file read position. It never stores or prints prompt text, code, file contents or project names.

State lives in `state.json` next to a `state.json.bak` of the previous save (`backyard doctor` shows the path).

<br>

## License

MIT. See [LICENSE](LICENSE).
