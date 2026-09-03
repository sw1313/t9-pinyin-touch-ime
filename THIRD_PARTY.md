# 第三方作品说明

本仓库的程序代码（`src/T9Pane`、`src/T9Ime` 等）以 **GNU GPL v3** 发布。
随程序分发的词库来自多个开源项目，各自保留原许可。它们都可以与 GPL v3 程序一起分发，但**不是**本项目原创词库。

词库文件放在 `src/T9Pane/Data/xiaobai-t9/`。目录名沿用当初的打包来源，并不表示本项目是「小白 T9」的分支。

## 打包来源

| 作品 | 许可 | 地址 |
| --- | --- | --- |
| 小白 T9 开源词库打包（`output/data/dicts/`） | GPL-3.0 | https://github.com/HuanSoft-Open-Source-Community/xiaobai-t9 |

本项目**没有**使用小白 T9 的 Rime/C++ 引擎或软键盘代码，只使用了其开源词库打包中的字典文件。本项目与小白 T9、HuanSoft 没有从属关系。

## 各字典文件

| 文件 | 文件头标明的来源 | 上游许可 |
| --- | --- | --- |
| `jichu.dict.yaml` | [amzxyz/RIME-LMDG](https://github.com/amzxyz/RIME-LMDG) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| `zi.dict.yaml` | 同上 | CC BY 4.0 |
| `duoyin.dict.yaml` | 同上 | CC BY 4.0 |
| `cn&en.dict.yaml` | 同上 | CC BY 4.0 |
| `en.dict.yaml` | [iDvel/rime-ice](https://github.com/iDvel/rime-ice) | GPL-3.0 |
| `en_ext.dict.yaml` | 同上 | GPL-3.0 |
| `word.dict.yaml` | [iDvel/rime-settings](https://github.com/iDvel/rime-settings)（现并入 rime-ice） | GPL-3.0 |
| `punctuation.dict.yaml` | 同上 | GPL-3.0 |
| `luna_pinyin.dict.yaml` | 由 [rime/rime-luna-pinyin](https://github.com/rime/rime-luna-pinyin) / pinyin_simp 精简 | LGPL-3.0 |
| `pinyin_simp_8105.dict.yaml` | 由 [rime/rime-pinyin-simp](https://github.com/rime/rime-pinyin-simp) 精简至《通用规范汉字表》8105 字 | Apache-2.0 |
| `shortcuts.dict.yaml` | 本仓库补充的网址/邮箱快捷短语 | GPL-3.0（随本项目） |

`en.dict.yaml` 在 rime-ice 中还注明合并了：

- [first20hours/google-10000-english](https://github.com/first20hours/google-10000-english)（公共领域词表）
- [tumuyan/rime-melt](https://github.com/tumuyan/rime-melt) 英文词（Apache-2.0）

CC BY 4.0 材料（万象 / RIME-LMDG）按原许可要求署名。本项目对上述词库的使用方式是：随输入法加载，未改编码格式；若条目有增删，以本仓库文件为准。

## 运行时依赖（不随源码再许可）

- .NET 8 Windows 桌面运行时（Microsoft）
- Windows TSF / UI Automation（Microsoft Windows SDK）

这些是系统或运行时接口，不构成本项目的衍生作品。
