# JSON format 1.0

TjsParser 的 JSON 是 AST 的跨语言表示，而不是执行 TJS 后得到的数据。

## 顶层

```json
{
  "schemaVersion": "1.0",
  "source": {
    "path": "main/envinit.tjs",
    "encoding": "cp932",
    "hasBom": false,
    "rootMode": "expression"
  },
  "document": {},
  "comments": [],
  "preprocessor": {
    "directives": [],
    "regions": [],
    "finalDefines": {}
  },
  "diagnostics": []
}
```

所有位置均基于解码后的 UTF-16 `.NET string` 字符偏移；`line` 和 `column` 从 1 开始，`end` 不包含在节点范围内。

## 节点

节点使用 `type` 区分具体类型，其余字段与 C# 强类型节点属性对应：

```json
{
  "type": "BinaryExpression",
  "span": {
    "start": { "offset": 0, "line": 1, "column": 1 },
    "end": { "offset": 5, "line": 1, "column": 6 }
  },
  "operator": "+",
  "left": {},
  "right": {}
}
```

整数和实数的 `value` 使用规范字符串，避免 JSON/JavaScript 对 64 位整数或特殊浮点值造成精度损失；`raw` 保留原始写法。

## 字典

字典使用条目数组：

```json
{
  "type": "DictionaryExpression",
  "entries": [
    {
      "type": "DictionaryEntry",
      "separator": "Arrow",
      "key": { "type": "LiteralExpression", "value": "name" },
      "value": { "type": "LiteralExpression", "value": "value" }
    }
  ]
}
```

`separator` 为 `Colon`、`Arrow` 或 `CommaPair`。条目保持源码顺序并允许重复键。

## 预处理

`PreserveAll` 模式解析所有条件块，同时在 `regions[].isActive` 中记录给定宏环境下的状态。`ActiveOnly` 模式用等长空白遮蔽未激活源码，因此未遮蔽节点的位置仍对应原文件。

预处理器只计算官方 `@set/@if/@endif` 使用的 Int32 表达式，不执行普通 TJS。

## 目录 manifest 1.1

CLI 递归解析目录时生成的 `manifest.json` 与单文件 AST JSON 使用独立的版本号。manifest 1.1 顶层包含 `fileCount`、`parsedCount`、`skippedCount`、`failedCount` 和 `success`。

每个 `files` 条目通过 `kind` 和 `status` 描述处理结果：

- 明文成功解析：`kind: "source-text"`、`status: "parsed"`，并提供 `encoding`、`rootMode` 和 `output`。
- 明文解析失败：`status: "failed"`，并提供 `errorCount` 和 `failure`。
- TJS2100 字节码：`kind: "tjs2100-bytecode"`、`status: "skipped"`、`output: null`，并提供 `skipReason`。

字节码跳过不计入 `failedCount`；只要 `failedCount` 为零，目录处理的 `success` 就为 `true`。
