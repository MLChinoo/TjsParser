# TjsParser

面向吉里吉里/KRKRZ TJS2 文件的 C# 解析器。核心库同时面向 `netstandard2.0` 和 `net8.0`，支持明文脚本的强类型 AST，以及 KBAD100 Dictionary/Array 二进制数据的递归值模型和 JSON 输出；不会执行脚本。

## 构建

```powershell
dotnet build TjsParser.sln -c Release
```

生成的库位于：

- `src/TjsParser/bin/Release/netstandard2.0/TjsParser.dll`
- `src/TjsParser/bin/Release/net8.0/TjsParser.dll`

`net8.0` 版本只依赖目标运行时。`netstandard2.0` 版本通过项目引用使用时会还原 `System.Text.Json` 和 `System.Text.Encoding.CodePages`；如果手工复制 DLL，也必须同时提供这些依赖。

如果 Windows 的 `dotnet` 命令错误指向没有 SDK 的 x86 Host，可显式运行 `C:\Program Files\dotnet\dotnet.exe`。

## C# 调用

```csharp
using TjsParser;
using TjsParser.Kbad;
using TjsParser.Serialization;

var result = Parser.ParseFile(@"D:\game\data\startup.tjs");
var json = AstJson.Serialize(result);

if (!result.Success)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}

if (Parser.DetectFileKind(@"D:\game\data\font\atlas.tjs") == TjsFileKind.Kbad100BinaryData)
{
    var document = KbadReader.ReadFile(@"D:\game\data\font\atlas.tjs");
    var dataJson = KbadJson.Serialize(document);
}
```

`Parser.ParseText` 用于已经解码成 .NET `string` 的源码。`Parser.ParseFile` 自动识别 UTF-8、UTF-16LE/BE BOM 和 CP932；也可以通过 `ParseOptions.EncodingHint` 强制指定。

`.tjs` 除了明文源码，还可能是以 `TJS2100\0` 开头的已编译 TJS2 字节码，或以 `KBAD100\0` 开头的二进制 Dictionary/Array 数据。使用 `Parser.DetectFileKind(path)` 区分格式；源码交给 `Parser.ParseFile`，KBAD 数据交给 `KbadReader.ReadFile`。`Parser.ParseFile` 仍只接受源码，直接传入任一二进制格式会抛出 `UnsupportedTjsFormatException`。本项目暂不反汇编 TJS2100 字节码。

## 命令行

单文件输出到 stdout：

```powershell
dotnet run --project src/TjsParser.Cli -- parse startup.tjs
```

单文件的 `-o` 可以是 JSON 文件，也可以是已存在的目录；目录形式会自动生成 `startup.tjs.json`：

```powershell
dotnet run --project src/TjsParser.Cli -- parse startup.tjs -o D:\output\tjs-json
```

递归转换目录：

```powershell
dotnet run --project src/TjsParser.Cli -- parse D:\game\data -o D:\output\tjs-json --compact
```

条件编译模式：

```powershell
dotnet run --project src/TjsParser.Cli -- parse D:\game\data -o D:\output\active `
  --preprocess active -D kirikiriz=1 -D DEBUG=0
```

主要参数：

- `--mode auto|script|expression`：选择文件根语法，默认为自动识别。
- `--preprocess preserve|active`：保留全部条件块，或仅解析激活代码。
- `-D NAME=VALUE`：设置预处理初始宏。
- `--encoding utf-8|utf-16le|utf-16be|cp932`：强制输入编码。
- `--kbad-json plain|typed`：KBAD 输出普通 key/value JSON，或保留类型和字节范围的无损 JSON；默认为 `plain`。
- `--compact`：输出紧凑 JSON。
- `--no-comments`：不把注释写入 JSON。

目录模式保持输入相对路径，以 `.tjs.json` 为扩展名，并生成汇总 `manifest.json`。明文源码和 KBAD100 数据都会生成 JSON，并分别标记为 `source-text`/`parsed` 和 `kbad100-binary-data`/`parsed`。TJS2100 字节码仍标记为 `tjs2100-bytecode`/`skipped`，跳过字节码不导致命令失败。源码诊断或 KBAD 格式错误会令 CLI 返回退出码 1。

## AST 与 JSON

解析器区分两种文档根节点：

- `ScriptDocumentSyntax`：普通 TJS 程序。
- `ExpressionDocumentSyntax`：以 `%[...]`、`[...]`、`(const)[...]` 或匿名函数为整文件内容的表达式。

JSON 顶层固定包含 `source`、`document`、`preprocessor`、`diagnostics`，默认还包含 `comments`。每个 AST 节点都有 `type` 和结束位置不包含在内的 `span`。TJS 字典始终输出有序 `entries`，不会压缩成 JSON object，因此能保留重复键、表达式键和分隔形式。

更完整的字段约定见 [JSON format](docs/json-format.md)。

KBAD 使用独立的 `KbadDocument`/`KbadValue` 模型，不伪装成源码 AST。默认 JSON 直接输出普通 object/array/key/value，便于 Python 等工具读取；null/void 分别还原为配套 TOML 使用的 `{"":1}`/`{"":0}`。KBAD 自身支持 Boolean 标签，但当前配对 `ctxfontprefs` 的 TOML 布尔值在编译后成为整数 `1/0`，无法仅凭这些 KBAD 无歧义恢复。`--kbad-json typed` 可输出保留 Dictionary 顺序、null/void 区别、整数精度、octet 类型和字节范围的无损结构。详细约定见 [KBAD JSON format](docs/kbad-json-format.md)。

## 测试真实语料

```powershell
$env:TJS_CORPUS_DIR = 'D:\game\data'
dotnet test TjsParser.sln -c Release
```

游戏文件仅从外部目录读取，不会复制到仓库中。语料测试会解析所有明文文件和 KBAD100 数据，并验证 TJS2100 字节码会在文本解码前被识别和拒绝。
