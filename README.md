# backyard

A tiny terminal farm that grows a little every time Claude Code finishes a task.

No network access, no background process. It stores only event kinds and timestamps.

It comes in two forms: a one-line summary for your statusline, and a full-screen TUI you open when you want to tend the farm.

**The statusline** (`backyard status`):

```text
[. , v Y _ _ _ _ _ _] │ 12G │ ~ 4m
```

This line sits in your Claude Code statusline. Each time Claude finishes a task that used a tool, your crops get watered.

**The farm window** (`backyard`):

<img width="1920" height="1080" alt="Image" src="https://github.com/user-attachments/assets/8c8fea2b-4966-4bbd-8b80-722cd976740b" />

Opens a separate TUI with the garden, shop and codex. Here you harvest, sell, plant better seeds, or buy another row.

<br>

## Features

- Single native binary that is safe to run on every statusline refresh.
- Grows only when a task that used a tool finishes. No usage-based rewards, no streaks, no wilting.

<br>

## Installing

Download the archive for your platform from [Releases](../../releases), unpack it, and put `backyard` on your `PATH`.

| Platform            | Archive                     |
| ------------------- | --------------------------- |
| Windows x64         | `backyard-win-x64.zip`      |
| Linux x64           | `backyard-linux-x64.tar.gz` |
| macOS Apple Silicon | `backyard-osx-arm64.tar.gz` |

Then add it to your Claude Code statusline in `~/.claude/settings.json`:

```json
{
  "statusLine": {
    "type": "command",
    "command": "backyard status",
    "refreshInterval": 1
  }
}
```

If you already have a statusline command, append `backyard status` to it instead of replacing it.

Run `backyard doctor` to check the setup.

<br>

## Usage

| Command              | What it does                                                             |
| -------------------- | ------------------------------------------------------------------------ |
| `backyard status`    | Prints the one-line summary used by the statusline.                      |
| `backyard`           | Opens the TUI: garden, shop, codex.                                      |
| `backyard doctor`    | Shows why tasks are not being detected: paths, last event, parse errors. |
| `backyard reset`     | Deletes the farm and starts over.                                        |
| `backyard uninstall` | Removes the state directory and the `backyard://` link.                  |

<br>

## How the farm grows

- Finishing a Claude Code task that used a tool waters every dry cell.
- A cell that is already watered ignores further tasks until it grows.
- Ripe crops are harvested and sold in the TUI.

<br>

## Privacy

backyard reads the Claude Code transcripts under `~/.claude/projects` to find "task finished" events.

It keeps only the event kind, the timestamp, and a per-file read position. It never stores or prints prompt text, code, file contents or project names.

State lives in a single `state.json` (`backyard doctor` shows the path).

<br>

## License

MIT. See [LICENSE](LICENSE).
