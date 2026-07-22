# KBAD JSON format

KBAD100 可以输出普通 key/value JSON 或无损带类型 JSON，不是 TJS 源码 AST。`KbadJsonShape.Plain` 是 API 和 CLI 的默认值。

## Plain

Plain 模式直接把 KBAD 根值写成 JSON 根值：

```json
{
  "ascender": 1,
  "characters": {
    "S": [0.492, 0.001, -0.012]
  }
}
```

映射规则：

- Dictionary → JSON object。
- Array → JSON array。
- String、Boolean (`0xC2`/`0xC3`) → 对应 JSON 原生类型。
- Integer → JSON number，完整 uint64 也按十进制原样写出。
- 有限 Real → JSON number；`NaN`、`Infinity`、`-Infinity` → string。
- Null (`0xC0`) → `{"": 1}`。
- Void (`0xC1`) → `{"": 0}`。
- Octet → Base64 string。

`{"": 1}` 和 `{"": 0}` 是从配套 `ctxfontprefs.toml` 验证出的编译桥接表示：前者编译成 KBAD null，后者编译成 KBAD void。Plain 模式沿用该表示，既保持普通 key/value JSON，又不会合并两种特殊值。

KBAD100 格式本身定义了 Boolean 标签 `0xC2/0xC3`，解析器会原样还原为 JSON boolean。不过目前四组 `ctxfontprefs.toml` 的实际编译结果都把 TOML `true`/`false` 分别写成普通 Integer `1`/`0`，因此仅凭这些 KBAD 文件无法再区分它们与原本写成 `1`/`0` 的整数。普通 JSON 消费者也通常无法保留重复对象键。需要完整 KBAD 类型、顺序和字节范围时使用 `KbadJsonShape.Typed` 或 CLI 参数 `--kbad-json typed`。

## Typed 顶层

```json
{
  "schemaVersion": "1.0",
  "source": {
    "path": "data/font/atlas.tjs",
    "format": "kbad100",
    "length": 7162,
    "trailingByteCount": 0
  },
  "document": {
    "type": "KbadDocument",
    "value": {}
  }
}
```

严格模式要求根值后立即到达文件末尾，因此 `trailingByteCount` 通常为 `0`。启用 `KbadReadOptions.AllowTrailingData` 时会保留尾随字节数量，但尾随内容不会进入值树。

## Typed 值节点

所有值都有 `type`，默认还包含字节范围 `span`：

```json
{
  "type": "Integer",
  "span": { "offset": 42, "length": 5 },
  "value": "4294967295"
}
```

类型映射：

- `Null`、`Void`：没有 `value`，两者保持区别。
- `Boolean`：JSON boolean。
- `Integer`：十进制字符串，可无损表示完整 uint64/int64 范围。
- `Real`：使用 round-trip 格式的字符串，也能表示 `NaN` 和无穷大。
- `String`：直接输出 Unicode 字符。
- `Octet`：`encoding` 固定为 `base64`，`value` 是 Base64 字符串。
- `Array`：通过 `elements` 保存有序元素。
- `Dictionary`：通过 `entries` 保存序列化顺序和潜在重复键。

Dictionary 条目同时记录完整范围和键范围：

```json
{
  "span": { "offset": 9, "length": 26 },
  "keySpan": { "offset": 9, "length": 17 },
  "key": "ascender",
  "value": {
    "type": "Real",
    "span": { "offset": 26, "length": 9 },
    "value": "1"
  }
}
```

`KbadJsonOptions.IncludeByteSpans = false` 可以省略全部 `span` 和 `keySpan`。
