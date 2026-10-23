# MackieTranslator v1.0

首个正式发布版。一款面向 Windows 的划词、截图和手动翻译工具：选中文字或框选屏幕，即可查看原文与译文。

## 主要功能

- 中文与英文/其他外语之间按文本自动判断翻译方向。
- 划词自动翻译、截图识别翻译、粘贴及手动编辑翻译。
- 默认 Bing，以及可配置的七家 AI API 服务。
- 最近20条翻译历史，本机加密保存，可载入、复制和清空。
- 字体、行距、主题、语言、置顶与编辑区比例调整。
- 逐渐呈现译文、请求缓存、节省 Token 选项、取消和重试。
- 临时离线取词自检与本地诊断。

## 发布前重点修复

修正截图后直接回到 PDF 拖选时，因窗口焦点切换而漏掉手势的问题；鼠标监听使用独立线程。处理历史清空与保存竞争、长历史重载、无文字截图、旧请求回填及长文本动画负担。详情和验证边界见 TESTING.md。

## 使用提示

默认 Bing 截图使用 Windows OCR，可能识别不准确；建议配置支持图像输入的其他 API 服务。AI 仍可能出错，且图片识别与翻译可能分别计费。不要提交敏感信息。密钥在本机通过当前 Windows 用户加密保存；具体数据流和限制见 SECURITY.md。

## 发布附件

- MackieTranslator-v1.0-Setup.exe：安装版。
- MackieTranslator-v1.0-Portable.zip：便携版，完整解压运行。
- MackieTranslator-v1.0-Source.zip：源码。

运行环境：Windows x64、.NET Framework 4.8；本地 OCR 依赖 Windows OCR 组件及已安装语言。未验证所有系统、应用和模型组合。

GitHub Release 标题建议：MackieTranslator v1.0 — 首个正式版；Tag：v1.0。
