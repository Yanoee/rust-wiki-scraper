<p align="center">
  <img src="https://capsule-render.vercel.app/api?type=waving&color=0:0A84FF,50:5E5CE6,100:BF5AF2&height=210&section=header&text=Rust%20Wiki%20Scraper&fontSize=54&fontColor=ffffff&fontAlignY=36&desc=It%20downloads%20pictures.&descSize=18&descAlignY=58&animation=fadeIn" alt="Rust Wiki Scraper" />
</p>

<p align="center">
  <img src="https://readme-typing-svg.demolab.com?font=Inter&weight=500&size=18&pause=1800&color=A1A1AA&center=true&vCenter=true&width=640&lines=Downloads+Rust+icons.;That+is+the+whole+job.;It+has+animations+anyway." alt="" />
</p>

<p align="center">
  <img src="docs/logo.png" width="96" alt="" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-1.0.0-0A84FF?style=for-the-badge" alt="version 1.0.0" />
  <img src="https://img.shields.io/badge/.NET-10-5E5CE6?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0A84FF?style=for-the-badge&logo=windows11&logoColor=white" alt="Windows 10 and 11" />
  <img src="https://img.shields.io/badge/license-MIT-30D158?style=for-the-badge" alt="MIT" />
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/badge/Download-RustWikiScraper.exe-0A84FF?style=for-the-badge&logo=github&logoColor=white" alt="Download" height="40" /></a>
</p>

<p align="center">
  <img src="docs/screens/download.png" width="880" alt="Download page" />
</p>

## What it is

A Windows app that downloads every Rust item icon, every world picture and the full item list from rusthelp.com, then files them into folders with a JSON file next to them.

I made it because I was bored and my Rust server's icons were a mess. A folder and twenty minutes would have fixed that. I went with this instead.

It could have scraped the whole wiki. Loot tables, recipes, raid costs, all of it. It doesn't. I keep that power under control.

## What it does

- Every item icon as a 512 × 512 PNG named by shortname. About 1,300 of them in roughly thirty seconds.
- World pictures. NPCs, crates, vendors. Around 200. You will use four.
- `catalog.json` with every item's in-game ID, name and description, plus a field for your own translation that it never touches.
- Notices when rusthelp renames something, so your translation doesn't quietly go stale.
- Checks what changed on rusthelp without downloading anything, for when you want to know but not commit.
- A library to browse everything you hoarded. Search it, copy IDs, export it to CSV, look at it.
- Frosted glass, floating icons, buttons that wobble. None of it was needed.

<p align="center">
  <img src="docs/screens/library.png" width="880" alt="Library page" />
</p>

## Using it

Grab `RustWikiScraper.exe` from Releases. Run it. Press Start.

Everything ends up next to the exe. No installer, no runtime, no account. That is the entire manual.

## catalog.json

```json
"rifle.ak": {
  "id": 1545779598,
  "name": { "en": "Assault Rifle", "tr": "Saldırı Tüfeği" },
  "desc": { "en": "High damage machine rifle.", "tr": "" },
  "icon": { "src": "rifle.ak.png", "sha256": "...", "updated": "2026-10-22" },
  "tr_check": false,
  "removed": false
}
```

- `en` always comes from rusthelp.
- `tr` is yours. Fill it, ignore it, it stays the way you left it.
- `tr_check` flips to `true` when the English name changes under you.
- Items rusthelp drops get `removed: true`. Nothing is ever deleted.

## Source

It's all here. Build it, fork it, rewrite it in Rust for the irony. I don't really care what you do with it.

<p align="center">
  <img src="docs/screens/settings.png" width="880" alt="Settings page" />
</p>

## Questions nobody asked

**Is this necessary?**
No.

**Why a desktop app?**
It seemed like the most effort.

**Will it get updates?**
If I get bored again. Historically, I do.

**Linux? Mac?**
No.

## Credits

Data and pictures come from [rusthelp.com](https://rusthelp.com). Rust and its artwork belong to Facepunch Studios. This project has nothing to do with either of them.

Icons are [Font Awesome Free](https://fontawesome.com) (CC BY 4.0, font under SIL OFL 1.1).

MIT licensed.

<p align="center">
  <img src="https://capsule-render.vercel.app/api?type=waving&color=0:0A84FF,50:5E5CE6,100:BF5AF2&height=110&section=footer" alt="" />
</p>
