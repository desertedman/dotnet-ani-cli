# dotnet-ani-cli

Watch anime straight from the terminal!

https://github.com/user-attachments/assets/72321900-eac2-4cb0-ae1e-bf3b6e483af7

Built off of <https://github.com/Slimyslushy/anikoto-cli>

Scrapes the MyAnimeList API for search results (and their website for episode names), and feeds it into the Anikoto/Megaplay API.

## Requirements

- mpv
- vlc support planned
- .NET runtime (only version 10 tested)
- Linux or Windows

VLC support is a work in progress. The episode will stream, but subtitles will not work.

May or may not work on MacOS, YMMV. I do not actively support Mac.

## Running

```bash
git clone https://github.com/desertedman/dotnet-ani-cli.git
cd dotnet-ani-cli
dotnet run
```

## Building a distributable

```bash
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
# or..
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Launch Options

- `--c` - Choose subtitle track. If not specified, uses the default subtitle
track (usually English)

## Disclaimer

Large amounts of assistance provided by AI. Used to:

- Understand original script
- Vibecode

## Libraries

- [HTML Agility Pack](https://html-agility-pack.net/)
