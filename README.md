# music_lyric_snyc_server

## 功能说明
- 通过 Windows SMTC 监听 QQ 音乐播放状态。
- 显示歌曲信息：封面、歌名、歌手、播放状态、播放进度。
- 通过 API 获取歌词并同步显示（支持当前行高亮与自动滚动）。
- 歌词匹配优化：按“歌曲名 + 歌手”匹配歌曲 ID，再获取歌词。
- 歌词时间提前 200ms，提升对齐体验。
- 提供播放控制：上一首 / 播放暂停 / 下一首。
- 支持托盘运行：最小化到右下角托盘，双击托盘恢复窗口。
- 内置日志面板：支持 INFO/WARN/ERROR 过滤与清空。
- 提供 UDP/TCP 服务用于外部客户端同步歌词状态。

## 运行环境
- Windows 10/11
- .NET 8 SDK

## 启动方式
```powershell
dotnet build
dotnet run
```

## 界面使用
- 左侧：封面、歌曲信息、播放进度、控制按钮。
- 右侧：当前歌词与完整歌词列表（自动高亮/滚动）。
- 底部：运行日志（可过滤级别、可清空）。

## 托盘行为
- 最小化：隐藏到托盘。
- 双击托盘图标：恢复窗口。
- 托盘菜单：
  - `打开`：恢复窗口
  - `退出`：退出程序
- 点击窗口右上角 `X`：正常关闭程序。

## 歌词 API
- 搜索歌曲：
  - `https://api.vkeys.cn/v2/music/tencent/search/song?word=歌曲名`
- 获取歌词：
  - `https://api.vkeys.cn/v2/music/tencent/lyric?id=歌曲id`
- 解析字段：使用 `data.lrc`。
致谢落月api【https://doc.vkeys.cn/】
## UDP/TCP 同步协议
### 1) UDP 发现（无客户端连接时）
- 服务监听 UDP 端口：`33332`
- 客户端发送：
  - `[who is server]`
- 服务回复：
  - 当前服务器 IPv4 地址（纯文本）

### 2) TCP 推送（发现后切换）
- 服务监听 TCP 端口：`33333`
- TCP 连接建立后，服务持续按行推送 JSON：

```json
{"title":"...","artist":"...","status":"Playing","position_ms":123,"duration_ms":210000,"lines":"sync lyrics"}
```

字段说明：
- `title`：歌曲名
- `artist`：歌手
- `status`：播放状态（Playing / Paused / Stopped）
- `position_ms`：当前进度（毫秒）
- `duration_ms`：总时长（毫秒）
- `lines`：当前同步歌词文本

### 3) 推送策略
- 暂停后 60 秒内继续推送。
- 暂停超过 60 秒停止推送。
- 恢复播放后自动恢复推送。
- TCP 断开后自动回到 UDP 监听模式。

## 常见问题
- 无法编译且提示 exe/dll 被占用：先关闭正在运行的程序实例后再 `dotnet build`。
- 未显示歌词：确认 QQ 音乐当前歌曲可通过 API 搜索到对应 ID，且 `data.lrc` 非空。
- 外部客户端收不到服务：确认防火墙放行 UDP `33332` 与 TCP `33333`。
