此目录用于存放使用说明文档（.html 优先，其次是 .pdf）。

主窗口菜单「帮助 → 使用文档」的打开规则：
- Win10 及以上且已安装 WebView2：用内置 WebView2 窗口打开此目录下第一个 .html（网页形式，目录与链接可点）；没有 .html 时回退 .pdf；
- 其他环境（含 Win7、未安装 WebView2）：用系统默认程序打开第一个 .pdf，没有 .pdf 时打开 .html；
- 两者都没有时，才回退到历史遗留的 .xps（DocumentViewer）。

维护提示：使用说明书的唯一源文件是仓库根目录的「使用说明书.md」。
改完源文件后执行 tools\build-manual.ps1，脚本会用 pandoc 生成自包含 HTML、再用 Edge 无头打印成 PDF，两个文件都输出到本目录；不再手工导出 XPS。

构建/发布时，此目录会随程序一起部署。
