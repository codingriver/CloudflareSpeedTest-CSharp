# Cloudflare Speed Test -- GUI 布局设计文档

基于项目 CloudflareSeedTest-CSharp 的参数体系和功能说明，设计桌面 GUI 布局方案。
采用左侧竖向导航 + 主内容区布局，共 8 个导航页。

---

## 整体窗口结构

```
+-------------------------------------------------------------------------------+
|  [*] Cloudflare Speed Test                              [-] [O] [X]           |
+--------------------+----------------------------------------------------------+
| [>] IP 来源        | （主内容区，随左侧导航切换）                              |
| [ ] 延迟测速       |                                                          |
| [ ] 下载测速       |                                                          |
| [ ] 定时调度       |                                                          |
| [ ] Hosts 更新     |                                                          |
| [ ] 输出设置       |                                                          |
| [ ] 其他设置       |                                                          |
| ----------------   |                                                          |
| [ ] 测速结果 [10]  |  <- 角标显示结果 IP 数量                                |
| ----------------   |                                                          |
| [▶ 开始测速     ]  |                                                          |
| [■  停  止      ]  |                                                          |
| [=========  ] 78%  |  <- 全局进度条，任意页可见                               |
| 下载测速中...      |                                                          |
|                    +----------------------------------------------------------+
|                    | @ 就绪 | 已测:-- | 耗时:-- | 当前最快:--               |
+--------------------+----------------------------------------------------------+
```

### 布局分区说明

| 区域       | 宽度占比 | 说明                                     |
|------------|---------|------------------------------------------|
| 左侧导航栏 | ~20%    | 固定宽度；竖向菜单+全局操作按钮+进度条   |
| 主内容区   | ~80%    | 随导航切换，显示对应配置页或结果页       |
| 底部状态栏 | 100%    | 固定在主内容区底部，一行显示实时状态摘要 |

---

## 全局数据结构绑定概述

所有页面的控件最终都读写同一个 `CfstOptions` 实例（以下简称 `opts`）。
GUI 层在点击「开始测速」时将 `opts` 序列化为命令行参数列表，传给 `CfstProcessManager`。

```
GUI 控件  <--双向绑定-->  CfstOptions (opts)  --序列化-->  命令行参数  -->  cfst.exe
```

**数据流向：**
1. 用户修改控件 → 立即写入 `opts` 对应字段
2. 点击「开始测速」→ `CfstOptionsExtensions.ToArgList(opts)` 生成参数列表
3. `CfstProcessManager.Start(args)` 启动进程，监听 stdout/stderr 输出
4. 进程输出解析后写入 `TestResult`，驱动结果页和状态栏刷新
5. 测速结束 → 侧栏角标、状态栏、结果页统一更新

---

## 左侧导航栏

```
+--------------------+
| [*] CFST           |  <- 应用标题 / Logo
+--------------------+
| [>] IP 来源        |  <- 当前激活页（高亮背景）
|  .  延迟测速       |
|  .  下载测速       |
|  .  定时调度       |
|  .  Hosts 更新     |
|  .  输出设置       |
|  .  其他设置       |
|  ----------------  |
|  .  测速结果 [10]  |  <- 角标：测速完成后显示结果 IP 数
+--------------------+
| [▶  开始测速    ]  |  <- 主操作按钮（绿色）
| [■    停  止    ]  |  <- 停止按钮（红色，运行中才激活）
| [=========  ] 78%  |  <- 全局进度条
| 下载测速中...      |  <- 全局状态文字
+--------------------+
```

### 导航栏交互逻辑

| 元素 | 交互行为 | 数据/状态来源 |
|------|---------|---------------|
| 导航菜单项 | 单击切换主内容区，激活项高亮；切换不影响 `opts` | `AppState.CurrentPage` |
| 测速结果 `[N]` 角标 | 测速完成后出现，数字 = `TestResult.IpList.Count`；点击跳转结果页 | `TestResult.IpList.Count` |
| **▶ 开始测速** | 点击 → 验证必填字段 → `ToArgList(opts)` → `CfstProcessManager.Start()` → 按钮禁用；进度条/状态文字开始刷新 | 触发 `CfstProcessManager` |
| **■ 停止** | 仅运行中可点；点击 → `CfstProcessManager.Stop()` → 等待进程退出 → 恢复开始按钮 | `AppState.IsRunning` |
| 全局进度条 | 绑定 `AppState.Progress`（0~100），由进程输出解析驱动 | `AppState.Progress` |
| 全局状态文字 | 绑定 `AppState.StatusText`，显示「延迟测速中...」「下载测速中...」「就绪」等 | `AppState.StatusText` |

**开始/停止按钮互斥逻辑：**

```
运行中:  开始按钮 Enabled=false,  停止按钮 Enabled=true
空闲中:  开始按钮 Enabled=true,   停止按钮 Enabled=false
```

---

## 底部状态栏

```
@ 就绪 | 已测: 486/2000 | 耗时: 01:31 | 当前最快: 50ms / 82.63 Mbps
```

| 字段 | 数据绑定 | 更新时机 |
|------|---------|----------|
| 运行状态 | `AppState.StatusText` | 阶段切换时（「就绪 / 延迟测速中 / 下载测速中 / 已完成 / 已停止」） |
| 已测 N/Total | `AppState.TestedCount` / `AppState.TotalCount` | 进程 stdout 每行解析一次 |
| 耗时 | `AppState.Elapsed`（UI 定时器每秒刷新） | 开始时启动，结束/停止时冻结，格式 `mm:ss` |
| 当前最快 | `AppState.BestLatency` / `AppState.BestSpeed` | 从已完成 IP 中实时取最小延迟/最大速度 |

---

## 页面 1 -- IP 来源

对应模块：`IpProvider.cs` / `Config.cs`

| 控件 | 绑定字段 | 默认值 |
|------|---------|--------|
| IPv4 文件路径 | `opts.IPv4File` | `"ip.txt"` |
| IPv6 文件路径 | `opts.IPv6File` | `"ipv6.txt"` |
| 直接指定 IP 段 | `opts.IpRanges` | `null` |
| IP 数量上限 | `opts.IpLoadLimit` | `0` |
| 全量扫描复选框 | `opts.AllIp` | `false` |

```
+-----------------------------------------------------------------------------+
|  IP 来源                                                                    |
| ---------------------------------------------------------------------------  |
|  IPv4 文件 (-f) :  [ip.txt                              ] [浏览...]        |
|  IPv6 文件 (-f6):  [ipv6.txt                            ] [浏览...]        |
|                                                                             |
|  直接指定 IP 段 (-ip)  <- 优先级高于文件，留空则读取文件                   |
|  +-------------------------------------------------------------------------+|
|  | 173.245.48.0/20,104.16.0.0/13                                           ||
|  +-------------------------------------------------------------------------+|
|  逗号分隔多个 CIDR 段，留空则读取上方 IP 文件                              |
|                                                                             |
|  IP 数量上限 (-ipn):  [_______0________]   0 = 不限制                     |
|  [_] 全量扫描 (-allip) -- 扫描每个/24段全部IP（默认每段随机取1个）         |
+-----------------------------------------------------------------------------+
```

### 控件交互逻辑

**IPv4 / IPv6 文件路径（TextBox）**
- 用户直接编辑 → 实时写入 `opts.IPv4File` / `opts.IPv6File`
- 「浏览...」按钮 → 打开 `OpenFileDialog`（过滤 `*.txt`）→ 选中后回写文本框并更新 `opts`
- 文件路径不存在时：文本框显示红色边框 + tooltip「文件不存在，将在运行时报错」
- `opts.IpRanges` 非空时：两个文件行右侧显示浅色提示「当前使用直接 IP 段，文件配置已忽略」（控件不禁用，便于用户随时切回）

**直接指定 IP 段（TextBox 多行）**
- 实时写入 `opts.IpRanges`；清空时写入 `null`
- 非空 → 触发联动：IPv4/IPv6 文件行加「已忽略」提示
- 格式校验：以逗号分割后验证每段是否为合法 CIDR；无效段以红色下划线标出

**IP 数量上限（NumericUpDown）**
- 绑定 `opts.IpLoadLimit`，最小值 0，步进 100
- 值为 0 时右侧显示灰色说明「0 = 不限制」

**全量扫描（CheckBox）**
- 绑定 opts.AllIp
- 鍕鹃€夋椂鏄剧ず姗欒壊璀﹀憡: 鎵弿閲忓ぇ骞呭鍔狅紝鑰楁椂鍙兘瓒呰繃10鍒嗛挓
- 寤鸿閰嶅悎 IP 鏁伴噺涓婇檺鎺у埗鑰楁椂


---

## 椤甸潰 2 -- 寤惰繜娴嬮€?
瀵瑰簲妯″潡锛欼cmpPinger.cs / PingTester.cs / HttpingTester.cs

| 鎺т欢 | 缁戝畾瀛楁 | 榛樿鍊?|
|------|---------|--------|
| 娴嬮€熸柟寮忓崟閫?| opts.PingMode | PingMode.IcmpAuto |
| 寮哄埗 ICMP | opts.ForceIcmp | false |
| 骞跺彂鏁?| opts.PingConcurrency | 200 |
| 鍗旾P娴嬮€熸鏁?| opts.PingCount | 4 |
| 寤惰繜涓婇檺 | opts.LatencyMax | 9999 |
| 寤惰繜涓嬮檺 | opts.LatencyMin | 0 |
| 涓㈠寘鐜囦笂闄?| opts.PacketLossMax | 1.0 |
| HTTPing 鐘舵€佺爜 | opts.HttpingCode | 0 |
| 鍦板尯鐮佽繃婊?| opts.CfColo | null |

### 鎺т欢浜や簰閫昏緫

**娴嬮€熸柟寮忥紙RadioButton 涓夐€変竴锛?*
- 缁戝畾 opts.PingMode锛圛cmpAuto / TcPing / Httping锛?- 閫変腑 IcmpAuto 鈫?寮哄埗ICMP澶嶉€夋 Enabled=true锛涘叾浣欎袱椤规椂缃伆骞跺悓姝?opts.ForceIcmp=false
- 閫変腑 Httping 鈫?HTTPing涓撳睘鍙傛暟鍖哄煙 Enabled=true锛涘叾浣欐ā寮忔椂鍏ㄩ儴缃伆锛堜笉娓呯┖鍊硷級
- 閫変腑 TcPing 鈫?寮哄埗ICMP鍜孒TTPing涓撳睘鍖哄煙鍧囩疆鐏?
**寮哄埗 ICMP锛圕heckBox锛?*
- 缁戝畾 opts.ForceIcmp
- 浠?opts.PingMode == IcmpAuto 鏃跺彲缂栬緫锛屽惁鍒欑疆鐏颁笖鍊煎己鍒朵负 false

**骞跺彂鏁帮紙NumericUpDown锛?*
- 缁戝畾 opts.PingConcurrency锛岃寖鍥?1~1000锛屾杩?50

**鍗旾P娴嬮€熸鏁帮紙NumericUpDown锛?*
- 缁戝畾 opts.PingCount锛岃寖鍥?1~20锛屾杩?1

**寤惰繜涓婇檺锛圢umericUpDown锛?*
- 缁戝畾 opts.LatencyMax锛岃寖鍥?1~99999锛屾杩?100
- 瀹炴椂鏍￠獙锛歀atencyMax 蹇呴』 > LatencyMin锛屽惁鍒欑孩妗嗘彁绀?
**寤惰繜涓嬮檺锛圢umericUpDown锛?*
- 缁戝畾 opts.LatencyMin锛岃寖鍥?0~99999锛屾杩?10
- 瀹炴椂鏍￠獙锛歀atencyMin 蹇呴』 < LatencyMax

**涓㈠寘鐜囦笂闄愶紙NumericUpDown锛?*
- 缁戝畾 opts.PacketLossMax锛岃寖鍥?0.0~1.0锛屾杩?0.1
- 鐣岄潰浠ョ櫨鍒嗘瘮鏄剧ず锛?.1 鏄剧ず涓?10%锛夛紝鍐欏叆 opts 鏃跺瓨鍌ㄤ负灏忔暟

**HTTPing 鏈夋晥鐘舵€佺爜锛圢umericUpDown锛?*
- 缁戝畾 opts.HttpingCode锛涗粎 PingMode == Httping 鏃?Enabled=true
- 鍊间负 0 鏃跺彸渚ф彁绀恒€屾帴鍙?200 / 301 / 302銆?
**鍦板尯鐮佽繃婊わ紙TextBox锛?*
- 缁戝畾 opts.CfColo锛涗粎 PingMode == Httping 鏃?Enabled=true
- 娓呯┖鏃跺啓鍏?null锛涜緭鍏ユ椂浠ラ€楀彿鍒嗗壊锛屾瘡娈佃浆澶у啓鍚庢牎楠屾槸鍚︿负 3 瀛楁瘝鏈哄満浠ｇ爜
- 鏃犳晥鍦板尯鐮佷互绾㈣壊涓嬪垝绾挎爣鍑?
---

## 椤甸潰 3 -- 涓嬭浇娴嬮€?
瀵瑰簲妯″潡锛歋peedTester.cs

| 鎺т欢 | 缁戝畾瀛楁 | 榛樿鍊?|
|------|---------|--------|
| 绂佺敤涓嬭浇娴嬮€?| opts.DisableDownload | false |
| 娴嬮€?URL | opts.DownloadUrl | https://speed.cloudflare.com/__down?bytes=52428800 |
| 娴嬮€熺鍙?| opts.DownloadPort | 443 |
| 鍙備笌娴嬮€烮P鏁?| opts.DownloadCount | 10 |
| 涓嬭浇瓒呮椂 | opts.DownloadTimeout | 10 |
| 閫熷害涓嬮檺 | opts.SpeedMin | 0 |

### 鎺т欢浜や簰閫昏緫

**绂佺敤涓嬭浇娴嬮€燂紙CheckBox锛?*
- 缁戝畾 opts.DisableDownload
- 鍕鹃€?鈫?椤甸潰鍐呮墍鏈夊叾浠栨帶浠跺叏閮?Enabled=false锛圲RL銆佺鍙ｃ€両P鏁般€佽秴鏃躲€侀€熷害涓嬮檺锛?- 鍙栨秷鍕鹃€?鈫?鍏ㄩ儴鎭㈠ Enabled=true锛屽悇瀛楁鍊间繚鎸佷笉鍙?
**娴嬮€?URL锛圱extBox锛?*
- 缁戝畾 opts.DownloadUrl
- 澶卞幓鐒︾偣鏃跺仛 URL 鍚堟硶鎬ч獙璇侊紙Uri.TryCreate锛夛紝闈炴硶鍒欑孩妗?tooltip
- URL鍗忚鑱斿姩绔彛鎻愮ず锛歨ttp:// 寮€澶存椂鎻愮ず銆屽缓璁皢绔彛鏀逛负 80銆嶏紱https:// 寮€澶存彁绀恒€屽缓璁鍙ｄ负 443銆?
**娴嬮€熺鍙ｏ紙NumericUpDown锛?*
- 缁戝畾 opts.DownloadPort锛岃寖鍥?1~65535锛屾杩?1
- 鍙抽敭鑿滃崟鎻愪緵銆岃涓?80銆嶃€岃涓?443銆嶅揩鎹烽€夐」

**鍙備笌娴嬮€烮P鏁帮紙NumericUpDown锛?*
- 缁戝畾 opts.DownloadCount锛岃寖鍥?1~100锛屾杩?1
- 鍙充晶鎻愮ず銆屼粠寤惰繜娴嬮€熼€氳繃鐨?IP 涓彇鍓?N 涓弬涓庝笅杞芥祴閫熴€?
**涓嬭浇瓒呮椂锛圢umericUpDown锛?*
- 缁戝畾 opts.DownloadTimeout锛屽崟浣嶇锛岃寖鍥?1~120锛屾杩?1

**閫熷害涓嬮檺锛圢umericUpDown锛?*
- 缁戝畾 opts.SpeedMin锛屽崟浣?MB/s锛屾渶灏?0锛屾杩?1
- 鍊间负 0 鏃跺彸渚ф彁绀恒€? = 涓嶈繃婊ゃ€?
---

## 椤甸潰 4 -- 瀹氭椂璋冨害

瀵瑰簲妯″潡锛歋cheduler.cs

| 鎺т欢 | 缁戝畾瀛楁 | 榛樿鍊?|
|------|---------|--------|
| 璋冨害妯″紡鍗曢€?| opts.ScheduleMode | ScheduleMode.None |
| 闂撮殧鍒嗛挓鏁?| opts.IntervalMinutes | 0 |
| 姣忔棩瀹氱偣鏃堕棿 | opts.DailyAt | null |
| Cron 琛ㄨ揪寮?| opts.CronExpression | null |
| 鏃跺尯 | opts.TimeZone | null锛堢郴缁熼粯璁わ級|

### 鎺т欢浜や簰閫昏緫

**璋冨害妯″紡锛圧adioButton 鍥涢€変竴锛?*
- 缁戝畾 opts.ScheduleMode锛圢one / Interval / Daily / Cron锛?- 閫変腑 None 鈫?鎵€鏈夊弬鏁版帶浠跺強鏃跺尯涓嬫媺鍧?Enabled=false锛涢瑙堝尯鏄剧ず銆?-銆?- 閫変腑 Interval 鈫?闂撮殧鍒嗛挓鏁?Enabled=true锛涘畾鐐?Cron/鏃跺尯 Enabled=false
- 閫変腑 Daily 鈫?瀹氱偣鏃堕棿 Enabled=true锛涢棿闅?Cron Enabled=false锛涙椂鍖?Enabled=true
- 閫変腑 Cron 鈫?Cron杈撳叆妗?Enabled=true锛涢棿闅?瀹氱偣 Enabled=false锛涙椂鍖?Enabled=true
- 鍒囨崲妯″紡鏃朵笉娓呯┖鏈縺娲绘帶浠剁殑鍊硷紝浠ヤ究鍒囧洖鏃舵仮澶?
**闂撮殧鍒嗛挓鏁帮紙NumericUpDown锛?*
- 缁戝畾 opts.IntervalMinutes锛岃寖鍥?1~10080锛?澶╋級锛屾杩?1
- 浠?ScheduleMode == Interval 鏃?Enabled

**姣忔棩瀹氱偣鏃堕棿锛圱extBox锛?*
- 缁戝畾 opts.DailyAt锛岄€楀彿鍒嗛殧澶氫釜鏃堕棿鐐癸紙濡?6:00,12:00,18:00锛?- 浠?ScheduleMode == Daily 鏃?Enabled
- 澶卞幓鐒︾偣鏃舵牎楠屾瘡涓椂闂寸偣鏍煎紡锛圚H:mm 鎴?H:mm锛夛紱鏃犳晥椤圭孩鑹蹭笅鍒掔嚎
- 鍚堟硶鏃跺埛鏂般€岃皟搴﹂瑙堛€嶅尯鍩?
**Cron 琛ㄨ揪寮忥紙TextBox锛?*
- 缁戝畾 opts.CronExpression
- 浠?ScheduleMode == Cron 鏃?Enabled
- 澶卞幓鐒︾偣鏃剁敤 Cron 瑙ｆ瀽搴撴牎楠岋紱鏃犳晥鍒欑孩妗?+ tooltip 鏄剧ず鍏蜂綋閿欒
- 鍚堟硶鏃跺埛鏂般€岃皟搴﹂瑙堛€嶅尯鍩?
**鏃跺尯锛圕omboBox锛?*
- 缁戝畾 opts.TimeZone锛涗粎 Daily / Cron 妯″紡鏃?Enabled
- 鍒楄〃鏉ユ簮锛歍imeZoneInfo.GetSystemTimeZones()锛岄椤逛负銆孡ocal -- 绯荤粺榛樿銆嶏紙鍊间负 null锛?- 閫変腑鏃跺埛鏂般€岃皟搴﹂瑙堛€?
**璋冨害棰勮锛堝彧璇?TextBlock锛?*
- 浠呭湪鍙傛暟鍚堟硶鏃舵樉绀恒€屼笅娆℃墽琛屻€嶃€屽啀涓嬫銆嶆椂闂达紝鐢?GUI 灞傛湰鍦拌绠楋紙涓嶄緷璧栧悗绔繘绋嬶級
- Interval 妯″紡锛氫笅娆?= 褰撳墠鏃堕棿 + N 鍒嗛挓
- Daily / Cron 妯″紡锛氫娇鐢?Cron 搴撹绠椾笅涓ゆ瑙﹀彂鏃堕棿
- 鍙傛暟闈炴硶鎴?None 鏃舵樉绀恒€?-銆?
---

## 椤甸潰 5 -- Hosts 鏇存柊

瀵瑰簲妯″潡锛欻ostsUpdater.cs

| 鎺т欢 | 缁戝畾瀛楁 | 榛樿鍊?|
|------|---------|--------|
| 鍚敤 Hosts 鏇存柊锛圕heckBox锛?| opts.HostsDomains != null | false锛坣ull锛墊
| 鐩爣鍩熷悕锛圱extBox锛?| opts.HostsDomains | null |
| 浣跨敤绗琋鍚岻P | opts.HostsIpRank | 1 |
| Hosts 鏂囦欢璺緞 | opts.HostsFile | null锛堢郴缁熼粯璁わ級|
| 浠呴瑙堜笉鍐欏叆 | opts.HostsDryRun | false |

### 鎺т欢浜や簰閫昏緫

**鍚敤 Hosts 鏇存柊锛圕heckBox锛?*
- 鏈嬀閫夋椂 opts.HostsDomains = null锛堝簭鍒楀寲鏃朵笉鐢熸垚 -hosts 鍙傛暟锛?- 鍕鹃€夋椂鐩爣鍩熷悕鏂囨湰妗嗐€両P鎺掑悕銆佹枃浠惰矾寰勩€佷粎棰勮澶嶉€夋鍏ㄩ儴 Enabled=true
- 鍙栨秷鍕鹃€夋椂浠ヤ笂鎺т欢鍏ㄩ儴 Enabled=false锛屼絾涓嶆竻绌哄€?
**鐩爣鍩熷悕锛圱extBox 澶氳锛?*
- 缁戝畾 opts.HostsDomains锛岄€楀彿鍒嗛殧锛屾敮鎸?* 閫氶厤绗?- 娓呯┖鏃?opts.HostsDomains = null锛堝悓姝ュ彇娑堛€屽惎鐢ㄣ€嶅閫夋鐘舵€侊級
- 鏍煎紡璇存槑锛? 閫氶厤绗﹀彧鏇存柊 hosts 涓凡鏈夌殑鍖归厤鏉＄洰锛屾棤鍖归厤鍒欎笉鏂板

**浣跨敤绗琋鍚岻P锛圢umericUpDown锛?*
- 缁戝畾 opts.HostsIpRank锛岃寖鍥?1~100锛屾杩?1
- 鍙充晶鎻愮ず銆屽皢娴嬮€熺粨鏋滄帓鍚嶇 N 鐨?IP 鍐欏叆 hosts銆?
**Hosts 鏂囦欢璺緞锛圱extBox锛?*
- 缁戝畾 opts.HostsFile锛涚暀绌烘椂鍐欏叆 null锛岃繍琛屾椂浣跨敤绯荤粺榛樿璺緞
- 銆屾祻瑙?..銆嶆寜閽墦寮€鏂囦欢閫夋嫨瀵硅瘽妗嗭紱Windows 榛樿瀹氫綅鍒?C:\Windows\System32\drivers\etc- 璺緞闈炵┖浣嗘枃浠朵笉瀛樺湪鏃舵樉绀洪粍鑹茶鍛婃銆岃矾寰勪笉瀛樺湪锛岃繍琛屾椂灏嗗皾璇曞垱寤恒€?
**浠呴瑙堜笉鍐欏叆锛圕heckBox锛?*
- 缁戝畾 opts.HostsDryRun
- 鍕鹃€夋椂鍦ㄦ帶浠舵梺鏄剧ず鎻愮ず銆屾祴閫熷畬鎴愬悗浠呭湪缁撴灉椤甸瑙堝緟鍐欏叆鍐呭锛屼笉淇敼绯荤粺 hosts銆?
**鏉冮檺鎻愮ず鍖猴紙鍙 InfoBar锛?*
- 濮嬬粓鏄剧ず锛屽唴瀹归殢 OS 鍔ㄦ€佸垏鎹細
  - Windows锛氥€岄渶浠ョ鐞嗗憳韬唤杩愯锛涙潈闄愪笉瓒虫椂鍐呭杈撳嚭鑷?hosts-pending.txt銆?  - Linux/macOS锛氥€岄渶 root 鐢ㄦ埛鎴?sudo 杩愯锛涙潈闄愪笉瓒虫椂鍐呭杈撳嚭鑷?hosts-pending.txt銆?- 鏉冮檺妫€娴嬶細鍚姩鏃舵娴嬪綋鍓嶈繘绋嬫槸鍚︿负绠＄悊鍛?root锛岃嫢鍚﹀垯鏉冮檺鎻愮ず琛屾樉绀烘鑹茶鍛婂浘鏍?
---

## 页面 6 -- 输出设置

对应模块：`OutputWriter.cs` / `ConsoleHelper.cs`

| 控件 | 绑定字段 | 默认值 |
|------|---------|--------|
| 输出文件路径 | `opts.OutputFile` | `"result.csv"` |
| 最终输出IP数 | `opts.OutputCount` | `10` |
| 静默模式 | `opts.Silent` | `false` |
| onlyip 文件路径 | `opts.OnlyIpFile` | `"onlyip.txt"` |

### 控件交互逻辑

**输出文件路径（TextBox）**
- 绑定 `opts.OutputFile`
- 「浏览...」按钮打开 `SaveFileDialog`（过滤 `*.csv`），选中后回写文本框和 `opts`
- 路径所在目录不存在时显示黄色警告「目录不存在，运行时将自动创建」

**最终输出IP数（NumericUpDown）**
- 绑定 `opts.OutputCount`，范围 1~1000，步进 1
- 右侧提示「控制台表格、CSV、onlyip.txt 均受此限制；传 0 或负数按 10 处理」

**静默模式（CheckBox）**
- 绑定 `opts.Silent`
- 勾选 → onlyip 文件路径行 `Enabled=true`；显示提示「只向 stdout 逐行输出 IP，不显示表格，适合脚本/管道调用」
- 取消勾选 → onlyip 文件路径行 `Enabled=false`

**onlyip 文件路径（TextBox）**
- 绑定 `opts.OnlyIpFile`；仅 `opts.Silent=true` 时 Enabled
- 「浏览...」打开 `SaveFileDialog`（过滤 `*.txt`）

**上次生成文件（只读预览）**
- 测速完成后自动刷新，显示文件名、修改时间、文件大小
- 「打开所在位置」按钮：Windows 调用 `explorer.exe /select,<path>`，macOS 调用 `open -R <path>`
- 文件不存在时该行显示「尚未生成」

---

## 页面 7 -- 其他设置

对应模块：`ConsoleHelper.cs` / `Program.cs` / `ProgressReporter.cs`

| 控件 | 绑定字段 | 默认值 |
|------|---------|--------|
| 调试输出 | `opts.Debug` | `false` |
| 结构化进度输出 | `opts.ShowProgress` | `false` |

### 控件交互逻辑

**调试输出（CheckBox）**
- 绑定 `opts.Debug`
- 勾选时在控件旁显示提示「启用后在运行日志中打印详细内部状态，便于排查异常」
- 值变化立即写入 `opts.Debug`；下次启动测速时生效

**结构化进度输出（CheckBox）**
- 绑定 `opts.ShowProgress`
- 勾选时序列化为 `-progress` 参数，cfst 进程在 stdout 输出 `PROGRESS:{json}` 行
- GUI 父进程通过 `CfstProcessManager.OnOutput` 事件过滤 `PROGRESS:` 前缀行并解析 JSON，驱动全局进度条和状态栏实时刷新
- 与静默模式（`-silent`）正交：两者可同时勾选，`-silent -progress` 时只输出 `PROGRESS:` 行和最终 IP 列表
- 消息类型说明（只读展开面板，鼠标悬停时显示）：

| `stageName` | `stageIndex` | 触发时机 |
|---|---|---|
| `init` | 0 | IP 列表加载完成 |
| `ping` | 1 | 延迟测速进行中（节流：每 1% 或每 50 个 IP）|
| `ping_done` | 1 | 延迟测速阶段结束摘要 |
| `speed` | 2 | 下载测速进行中 |
| `speed_done` | 2 | 下载测速阶段结束摘要 |
| `output` | 3 | 写 CSV / Hosts / onlyip 完成 |
| `done` | 4 | 本轮全部完成，含完整结果列表 |
| `error` | -1 | 错误（`errorCode`: `NO_IPS` / `CANCELLED` / `EXCEPTION`）|
| `schedule_wait` | -1 | 定时调度等待下次执行 |

**运行日志（内嵌控制台 TextBox，只读，自动滚动）**
- 数据来源：`CfstProcessManager.OnOutput` / `OnError` 事件回调
- 每条日志追加一行，格式：`[HH:mm:ss] 内容`
- `opts.Debug=true` 时额外显示 `[DEBUG]` 前缀行（灰色区分正常输出）
- 自动滚动到末尾；用户手动向上滚动时暂停自动滚动，滚回底部时恢复
- 「清空日志」按钮：清空日志文本框内容，不影响进程输出
- 「复制全部日志」按钮：将全部内容写入剪贴板，按钮短暂显示「已复制」后恢复原文字

**关于区（只读）**
- 显示版本号（从程序集版本读取）、License、上游项目链接
- 「项目主页」为可点击超链接，调用 `Process.Start` 打开浏览器

---

## 页面 8 -- 测速结果

对应模块：`IPInfo.cs` / `OutputWriter.cs` / `HostsUpdater.cs` / `SyncProgress.cs`

### 数据来源

| 数据 | 来源字段 |
|------|----------|
| 扫描IP总数 | `AppState.TotalCount` |
| 有效IP数 | `AppState.PassedCount` |
| 最快延迟 | `TestResult.IpList[0].AvgDelay` |
| 最高速度 | `TestResult.IpList[0].DownloadSpeed` |
| 完成时间 | `AppState.FinishTime` |
| 耗时 | `AppState.Elapsed`（最终值）|
| 结果列表 | `TestResult.IpList`（`IPInfo` 列表）|
| Hosts写入状态 | `HostsUpdater.LastWriteResult` |

### 控件交互逻辑

**概况区（只读 Label）**
- 测速完成后一次性填充，运行中显示「测速进行中...」占位
- 完成时间和耗时从 `AppState` 读取

**筛选/排序栏**
- 地区码下拉（ComboBox）：列表从 `TestResult.IpList` 中提取所有不重复的 `DataCenter` 值，首项为「全部」
- 延迟上限（NumericUpDown）：输入后实时过滤列表，空白表示不过滤
- 速度下限（NumericUpDown）：输入后实时过滤列表，空白表示不过滤
- 排序下拉（ComboBox）：选项为「延迟升序、延迟降序、速度升序、速度降序、丢包率升序」；默认「延迟升序」
- 「应用筛选」按钮：将当前筛选条件应用到列表视图
- 筛选/排序不修改 `TestResult.IpList` 原始数据，仅影响视图展示

**结果列表（DataGrid）**
- 绑定 `TestResult.IpList`（经筛选/排序后的视图）
- 列：排名、IP地址、丢包率、平均延迟、下载速度、地区码、地区名
- 支持列头点击排序（单击升序、再次点击降序）
- 行单击 → 选中行高亮，右键菜单提供「复制 IP」「复制整行」选项
- 列表为空时显示「暂无结果，请先运行测速」占位文字

**操作按钮**
- 「复制第1名IP」：将 `TestResult.IpList[0].IP` 写入剪贴板；列表为空时 `Enabled=false`
- 「复制全部IP」：将所有行的 IP 按换行拼接后写入剪贴板
- 「导出 CSV」：打开 `SaveFileDialog`，默认文件名为 `opts.OutputFile`，保存当前筛选/排序后的结果
- 「重新测速」：等同于点击侧栏「开始测速」按钮，快捷入口

**Hosts 写入预览区**
- `opts.HostsDryRun=true` 时标题显示「Hosts 写入预览（仅预览，未实际写入）」
- `opts.HostsDryRun=false` 且写入完成后标题显示「Hosts 已写入」
- 列表内容：IP 地址 + 域名，来源于 `HostsUpdater.LastWriteResult`
- 状态行：
  - 绿色勾「已成功写入系统 Hosts」
  - 橙色感叹号「权限不足，已输出至 hosts-pending.txt，可手动合并」
  - 未启用 Hosts 更新时此区域隐藏

---

## 设计决策总结

| 设计决策 | 说明 |
|---------|------|
| 左侧竖向导航 | 8个页面用竖向菜单比横向Tab更清晰，标签文字完整显示，扩展性强 |
| 结果页独立成导航项 | 结果表需要全宽展示，独立成页比压缩在右侧面板可读性更高 |
| 结果角标 [N] | 测速完成后在导航项显示IP数量，用户无需切页即可感知结果 |
| 全局进度条固定侧栏 | 无论在哪个配置页，进度始终可见，无需切换到结果页才能感知 |
| 开始/停止固定侧栏底部 | 全局可操作，无需切换到特定页面才能触发测速 |
| 日志内嵌到「其他设置」页 | 符合调试语义，不单独占导航项，减少导航层级 |
| HTTPing 参数条件激活 | 地区码过滤等参数仅在 HTTPing 模式选中时可用，避免误配置 |
| -dd 勾选后子参数置灰 | 禁用下载测速后相关参数自动不可编辑，防止无效设置 |
| Hosts 写入回显在结果页 | 测速完成后 hosts 状态和 IP 表在同一页，操作形成闭环 |
| 调度模式四选一单选组 | 不启用/间隔/定点/Cron 互斥，切换时对应输入框才激活 |
| 调度预览时间（只读） | 实时展示下次/再下次执行时间，用户可即时验证配置正确性 |
| 静默模式子选项联动 | 勾选静默模式后 onlyip 路径输入框才激活，减少界面噪音 |
| 控件值保留不清空 | 切换模式/取消勾选时不清空对应控件值，便于用户切回时恢复配置 |
| 校验反馈就地显示 | 格式错误以红框+tooltip 就地提示，不打断用户操作流程 |
| `-progress` 与 `-silent` 正交 | 两者可同时启用；`-progress` 驱动 GUI 进度条，`-silent` 抑制人类可读输出，互不干扰 |
| 进度消息封装在 `ProgressReporter` | 所有 `PROGRESS:` 输出集中在单一类，便于格式变更时只改一处 |

---

## 导航页与代码模块对照

| 导航页 | 对应源文件 | 核心参数 |
|--------|-----------|----------|
| IP 来源 | `IpProvider.cs` `Config.cs` | `-f` `-f6` `-ip` `-ipn` `-allip` |
| 延迟测速 | `IcmpPinger.cs` `PingTester.cs` `HttpingTester.cs` | `-n` `-t` `-tl` `-tll` `-tlr` `-tcping` `-httping` `-icmp` `-cfcolo` |
| 下载测速 | `SpeedTester.cs` | `-dd` `-url` `-tp` `-dn` `-dt` `-sl` |
| 定时调度 | `Scheduler.cs` | `-interval` `-at` `-cron` `-tz` |
| Hosts 更新 | `HostsUpdater.cs` | `-hosts` `-hosts-ip` `-hosts-file` `-hosts-dry-run` |
| 输出设置 | `OutputWriter.cs` `ConsoleHelper.cs` | `-o` `-p` `-silent` `-q` `-onlyip` |
| 其他设置 | `Program.cs` `ConsoleHelper.cs` `ProgressReporter.cs` | `-debug` `-progress` |
| 测速结果 | `IPInfo.cs` `OutputWriter.cs` `SyncProgress.cs` | -- |
