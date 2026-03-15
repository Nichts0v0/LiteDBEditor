# LiteDB Editor 

[简体中文](README.md) | [English](README_en.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Version](https://img.shields.io/badge/Version-0.1.5-blue.svg)](https://github.com/Nichts0v0/LiteDBEidtor/releases)

A lightweight, responsive LiteDB database visualization management tool built with **Avalonia 11** and **.NET 9**.

---

## Core Features

- [x] **Data Visualization Management**: CRUD data directly in the grid. <sub>~~Seriously, why doesn't LiteDB.Studio have this feature?😤~~</sub>.
- [x] **Smart Template Binding**: Support for importing C# class definitions as data templates. <sub>No more worrying about messing up my JSON anymore😋.</sub>
- [x] **Dynamic Property Configuration**: Reflection-based dynamic UI supporting complex nested types. <sub>Though it might struggle with super complex stuff😅.</sub>
- [x] **Single-File Portable Execution**: Dependency-free, high-performance Win-x64 executable. <sub>But the file is **STUPIDLY HUGE**😨.</sub>

---

## Screenshots

> [!TIP]
> Let's check out what the editor can do😗

### 1. Collections

 **Create Collection** (Creating a collection also generates a template with the same name) <sub>ps: You can also select C# scripts from files</sub>
 ![Demo](./screenshots/create_table.gif) 

 **Modify Collection** (Modifying a collection updates the corresponding template)
 ![Demo](./screenshots/change_table.gif) 

<sub>~~Feature Highlights below🥵~~</sub>
### 2. Data Templates

 **Internal Helper Classes**: Create internal classes to store complex data.
 ![Demo](./screenshots/inner_class.gif) 

 **Use Templates**: Create new collections using existing templates.
 ![Demo](./screenshots/use_old.gif) 

### 3. Data Operations 😋

 **Add Data**
 ![Demo](./screenshots/add_data.gif) 

 **Modify Data**
 ![Demo](./screenshots/change_data.gif) 

 ---

## How to Use (Quick Start)

1.  Go to the 👉 [Releases](https://github.com/Nichts0v0/LiteDBEidtor/releases) page.
2.  Download the latest version of `LiteDBEditor` for your system <sub>(though there's probably only one version anyway)</sub>.
3.  Click "Open Database" to select or create a `.db` file.
4.  Follow the guide in the UI preview and do whatever you want 🤪.

---

## Tech Stack

- **UI Framework**: [Avalonia 11](https://avaloniaui.net/)
- **Runtime**: .NET 9 (Single File Publish)
- **Database**: [LiteDB 5.x](https://www.litedb.org/)
- **Theme**: [Semi.Avalonia](https://github.com/irihans/Semi.Avalonia)
- **Service**: 
  - `Roslyn` (C# Source Parsing)
  - `CommunityToolkit.Mvvm`

---

## License

This project is licensed under the [MIT License](LICENSE).

---

<div align="center">
  Made with ❤️ by <b>Nichts Studio</b> & functional a bunch of AIs🙈
</div>
<div align="right">
<sub>ps: Fighting with AIs💪 is so exhausting too.</sub> 
</div>
