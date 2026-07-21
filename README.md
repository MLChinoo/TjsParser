# TjsParser

面向吉里吉里/KRKRZ TJS2 明文脚本的 C# 解析器。核心库同时面向 `netstandard2.0` 和 `net8.0`，提供强类型 AST、源码位置、注释、条件编译信息和 JSON 输出；不会执行脚本。

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
using TjsParser.Serialization;

var result = Parser.ParseFile(@"D:\game\data\startup.tjs");
var json = AstJson.Serialize(result);

if (!result.Success)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}
```

`Parser.ParseText` 用于已经解码成 .NET `string` 的源码。`Parser.ParseFile` 自动识别 UTF-8、UTF-16LE/BE BOM 和 CP932；也可以通过 `ParseOptions.EncodingHint` 强制指定。

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
- `--compact`：输出紧凑 JSON。
- `--no-comments`：不把注释写入 JSON。

目录模式保持输入相对路径，以 `.tjs.json` 为扩展名，并生成汇总 `manifest.json`。只要任一文件存在错误级诊断，CLI 返回退出码 1。

## AST 与 JSON

解析器区分两种文档根节点：

- `ScriptDocumentSyntax`：普通 TJS 程序。
- `ExpressionDocumentSyntax`：以 `%[...]`、`[...]` 或 `(const)[...]` 为整文件内容的数据表达式。

JSON 顶层固定包含 `schemaVersion`、`source`、`document`、`preprocessor`、`diagnostics`，默认还包含 `comments`。每个 AST 节点都有 `type` 和结束位置不包含在内的 `span`。TJS 字典始终输出有序 `entries`，不会压缩成 JSON object，因此能保留重复键、表达式键和分隔形式。

更完整的字段约定见 [JSON format](docs/json-format.md)。

## 测试真实语料

```powershell
$env:TJS_CORPUS_DIR = 'D:\game\data'
dotnet test TjsParser.sln -c Release
```

游戏文件仅从外部目录读取，不会复制到仓库中。
