# REE.Unpacker 变更记录（会话修改汇总）

本文档记录针对解包流程、平台目标与「仅已知文件」等行为的一次性修改（约 2026-04）。

---

## 1. 解包主循环：异常处理与继续执行

**文件：** `FileSystem/Package/PakUnpack.cs`

- 在遍历每个 `PakEntry` 的 `try` 块中执行解压与写入；在 `catch` 中分别处理 `OutOfMemoryException` 与 `Exception`。
- 捕获后在控制台以**红色**输出警告，内容包含当前文件的完整路径与条目哈希（十六进制）。
- 在 `catch` 中调用 `GC.Collect()` 与 `GC.WaitForPendingFinalizers()`，然后使用 `continue` 处理下一个条目，不因单个文件失败而终止整个解包。
- 在上述两个 `catch` 中还会将**被跳过条目的哈希**（每行一个 16 位大写十六进制，无 `0x`）追加写入可执行文件同目录下的 **`error_log.txt`**；写日志失败时静默忽略，不中断解包。
- 移除循环末尾多余的 `TPakStream.Dispose()`（已由 `using` 负责释放）。

---

## 2. 64 位偏移与读取、文件流

**文件：**

- `FileSystem/Package/PakUnpack.cs`
- `FileSystem/Helpers/Helpers.cs`
- `FileSystem/Package/PakUtils.cs`

**要点：**

- 使用显式 `FileStream` 打开 PAK：`FileMode.Open`、`FileAccess.Read`、`FileShare.Read`、较大缓冲区、`FileOptions.SequentialScan`，保证以 `Int64` 进行 `Seek`。
- 条目表总字节数使用 `(Int64)m_Header.dwTotalFiles * dwEntrySize` 计算；若超出 `int.MaxValue` 或无效，则报错并返回，避免 `int` 乘法溢出或无法放入单个 `byte[]`。
- 在 `Helpers` 中新增 `ReadBytes(this Stream stream, long count)`：在合法范围内委托给原有 `int` 版本；超出 `int.MaxValue` 时抛出 `ArgumentOutOfRangeException`，避免将 `dwCompressedSize` 强转为 `Int32` 时静默截断。
- `PakUtils.iWriteByChunks` 改为通过 `FileStream`（写、`SequentialScan`）与 `BinaryWriter` 写出，并去掉多余的 `Dispose()` 调用。

**说明：** 单块解压仍受 .NET `byte[]` 最大长度（`int.MaxValue`）限制；超过该大小的条目需另行设计流式解压，当前改动重点是**偏移与长度用 64 位表示**并避免错误截断。

---

## 3. 只提取「字典中有路径」的已知文件

**文件：**

- `FileSystem/Package/PakList.cs`
- `FileSystem/Package/PakUnpack.cs`

**要点：**

- 新增 `PakList.iContainsHash(UInt64 dwHash)`，基于工程列表字典 `m_HashList.ContainsKey` 判断条目是否「已知」。
- 在主循环中，在计算 `dwEntryHash` 之后、**任何**路径解析、`Seek`、读数据或解压**之前**：若 `!PakList.iContainsHash(dwEntryHash)`，则打印 `[跳过] 未知文件: {hash}`（16 位大写十六进制），并 `continue`。
- 未知条目不再分配解压缓冲区或写入磁盘，降低因未知大文件导致的内存压力。

**注意：** 若未成功加载对应 `.list` 或列表为空，则所有条目都会被视为未知并被跳过。

---

## 4. Zstandard.Net 加载失败（格式不正确）与工程平台

**文件：** `REE.Unpacker.csproj`

**现象：** 运行时提示无法加载 `Zstandard.Net`，或「试图加载格式不正确的程序」。

**原因：** 工程为 `AnyCPU` 且历史上易以 **32 位**进程运行，而 `Libs` 中的 `Zstandard.Net.dll` / `libzstd.dll` 为 **64 位**，架构不一致。

**修改：**

- `Debug|AnyCPU` 与 `Release|AnyCPU`：`PlatformTarget` 设为 `x64`，`Prefer32Bit` 设为 `false`。
- `Debug|x64` 与 `Release|x64`：将 `Prefer32Bit` 从 `true` 改为 `false`（与 64 位目标一致）。

发布后请使用 `bin\Release\`（或对应输出目录）下的 **64 位** `REE.Unpacker.exe`，并与 `Zstandard.Net.dll`、`libzstd.dll` 放在同一目录。

---

## 5. 编译

使用 Visual Studio 或 MSBuild 的 **Release** 配置编译解决方案 `REE.Unpacker.sln` 即可；默认 **Release | Any CPU** 现已产出 **x64** 可执行文件。
