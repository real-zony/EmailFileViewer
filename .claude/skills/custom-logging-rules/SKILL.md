---
name: custom-logging-rules
description: 在编写、审查或调整任何日志代码（ILogger / Serilog）的时候触发，也包括线上日志噪音治理、日志级别调整、给方法“补日志”这类需求。涵盖记录与否的判定准则、级别语义、结构化消息模板、异常记录归属、上下文关联与性能约束。
---

# 日志规范（Rule of Silence）

## 核心原则

**沉默是金。日志的默认答案是「不记」。**

一条日志只有同时满足三点才有资格存在：有明确的消费者、能触发明确的行动、这件事没有被别的地方记过。三条缺一条就删掉。

无人消费的日志不是信息，是噪音。噪音的代价不是磁盘，是**真正的错误被淹没**——当一次正常下单产出 12 条 Information，那条唯一重要的 Error 就没人看得见了。

### 职责边界（最常见的误用根源）

模型和人最容易把日志当成可观测性的万能替代品。记住三者分工：

| 需求 | 归谁 | 不要用日志做 |
|---|---|---|
| 还原调用链路、看每步耗时 | Tracing（Activity / OpenTelemetry） | ❌ 每步打一条 Information「还原时间线」 |
| 统计频率、成功率、趋势、告警阈值 | Metrics（计数器 / 直方图） | ❌ 打 Information 然后在日志平台上数条数 |
| 定位慢请求、性能分析 | APM / Tracing | ❌ 手写 `Stopwatch` 把耗时打进日志 |
| 记录异常与不可逆事实 | **日志** | ✅ 这才是日志的本职 |

> 「打点日志方便以后排查」「先记着万一有用」——这两句话出现时，说明你要的是 tracing 或 metrics，不是日志。

## 工作流：写下一条日志前，依次自问

1. **谁看？** —— 答不出具体的人（值班运维 / 排障工程师 / 审计），删掉。
2. **看了做什么？** —— 答不出具体行动（告警、回滚、联系上游、人工补数），删掉。
3. **别处记过没有？** —— HTTP 出入口有 `UseSerilogRequestLogging`、SQL 有 FreeSql、消息消费有 MassTransit、异常最终有全局处理器。已覆盖的，删掉。
4. **这是成功路径吗？** —— 是，且不是系统级状态变迁 / 审计事件，删掉。
5. **级别选对了吗？** —— 对照下表。

## 硬规则

### 级别语义

| 级别 | 含义 | 典型场景 |
|---|---|---|
| `Fatal` | 进程无法继续，或数据已不一致 | 启动失败、补偿逻辑本身失败导致账实不符 |
| `Error` | 已经发生、需要人介入，**且此处是最终处理点** | 未预期异常、外部依赖重试耗尽、被吞掉的异常 |
| `Warning` | 系统降级或偏离预期，但已自行处理 | 回退到默认配置、熔断打开、幂等冲突、数据异常但可继续 |
| `Information` | **仅**系统级状态变迁与审计事件 | 服务启动完成、后台任务启停、权限变更、管理员敏感操作 |
| `Debug` / `Verbose` | 仅开发期，禁止用于生产热路径 | 本地排查 |

`Information` 不是「重要的业务成功」的意思。**业务成功路径一律不记日志。**

### 必须沉默的清单

以下写法一律禁止，不接受「方便排查」作为理由：

- ❌ 方法出入口日志：`开始处理 XXX` / `XXX 处理完成`
- ❌ 成功路径 Information：`订单创建成功`、`库存扣减成功`、`通知发送成功`
- ❌ 循环体内逐条日志——N 条明细产出 N 条日志，是最严重的噪音放大
- ❌ 分段进度日志：`开始扣减库存，共 {Count} 条` / `全部扣减完成`
- ❌ 入参全量回显、DTO / 实体整体 dump
- ❌ 重试过程中的每次尝试（`第 2/3 次尝试`）——只有**重试耗尽**才值得记
- ❌ 手写 `Stopwatch` 记录耗时
- ❌ 手工生成 `TraceId = Guid.NewGuid()`——链路标识来自 `Activity.Current` 或中间件 enricher
- ❌ 上层已覆盖的内容：SQL 语句、HTTP 请求响应、消息收发

```csharp
// ❌ 基线上最典型的一段：6 条日志，0 条有消费者
_logger.LogInformation("开始处理 OrderPaidEvent，OrderId={OrderId}", msg.OrderId);
_logger.LogInformation("订单状态更新成功。OrderId={OrderId}", order.Id);
_logger.LogInformation("开始扣减库存，共 {ItemCount} 个明细", order.Items.Count);
foreach (var item in order.Items)
{
    await _inventoryService.DeductStockAsync(item.ProductId, item.Quantity, ct);
    _logger.LogInformation("库存扣减成功。ProductId={ProductId}", item.ProductId);
}
_logger.LogInformation("全部库存扣减完成。OrderId={OrderId}", order.Id);
_logger.LogInformation("OrderPaidEvent 处理完成。OrderId={OrderId}", order.Id);

// ✅ 正常流程全程沉默，只有异常才发声
foreach (var item in order.Items)
{
    await _inventoryService.DeductStockAsync(item.ProductId, item.Quantity, ct);
}
```

### 必须记录的清单

反过来，以下情况**必须**有日志，缺失同样是缺陷：

- ✅ **被吞掉的异常**——`catch` 之后不重新抛出的，必须留痕，否则是静默失败
- ✅ 外部依赖重试耗尽后的最终失败
- ✅ 数据不一致、补偿失败、账实不符（`Fatal`）
- ✅ 配置缺失并回退到默认值、降级 / 熔断触发（`Warning`）
- ✅ 安全与审计事件：登录失败、越权尝试、权限变更、管理员敏感操作

```csharp
// ✅ 吞异常必须留痕——这是旁路能力，失败不影响主流程，但不能无声无息
try
{
    await _notificationService.SendAsync(order.UserId, title, content, ct);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "站内信通知发送失败，已忽略，不影响订单主流程");
}
```

### 异常记录归属

**一次异常，全链路只记一次，记在最终处理它的地方。**

- ❌ `catch-log-rethrow`：记完又 `throw`，上层必然再记一次，同一个异常在日志里出现三四遍
- ❌ 兜底 `catch (Exception ex) { _logger.LogError(...); throw; }`——交给全局异常处理器
- ❌ `_logger.LogError("失败：{Msg}", ex.Message)`——丢掉堆栈；异常对象必须作为**第一个参数**传入
- ❌ `BusinessException` 这类预期业务校验失败**不记日志**：它通过 4xx 返回给了调用方，调用方已经知道了；要统计频率用 metrics。只有当它暗示系统或数据出了问题时才记 `Warning`

```csharp
// ❌ 抛出前记一次，外层 catch 再记一次，同一件事两条日志
if (stock.Stock < quantity)
{
    _logger.LogWarning("创建订单失败：库存不足，ProductId={ProductId}", productId);
    throw new BusinessException("INSUFFICIENT_STOCK", "库存不足");
}

// ✅ 预期的业务校验失败，直接抛，不记日志
if (stock.Stock < quantity)
{
    throw new BusinessException("INSUFFICIENT_STOCK", "库存不足");
}
```

### 结构化写法

- 消息正文用**中文**，`{属性名}` 用**英文 PascalCase**，同一概念全系统同名（订单永远是 `OrderId`，不要出现 `orderId` / `OrderNo` 混用）
- 禁止字符串插值 `$"..."` 和 `+` 拼接——破坏结构化，且级别关闭时仍然分配内存
- 关联标识走 `LogContext.PushProperty`，**已经 push 过的属性不要再写进消息模板**
- `@` 析构符只用于小的值对象；禁止 `@` 整个实体、DTO 或集合。集合只记 `Count` 和必要的 Id
- 不要把异常拼进模板，用带 `Exception` 参数的重载

```csharp
// ❌ 插值破坏结构化；PushProperty 过的 OrderId 又在模板里重复了一遍
using (LogContext.PushProperty("OrderId", order.Id))
{
    _logger.LogWarning($"支付预授权重试 {MaxRetries} 次后失败。OrderId={order.Id}");
}

// ✅ 模板只说这次特有的信息，关联字段交给 LogContext
using (LogContext.PushProperty("OrderId", order.Id))
{
    _logger.LogError(ex, "支付预授权重试 {MaxAttempts} 次后仍然失败", MaxRetries);
}
```

### 性能

- 热路径构造日志参数前先判断 `_logger.IsEnabled(LogLevel.Debug)`
- 高频日志使用 `[LoggerMessage]` 源生成的 `partial` 方法，避免装箱与模板解析开销

## 合理化借口对照表

基线测试中模型真实说出的理由，以及为什么它们站不住：

| 借口 | 现实 |
|---|---|
| 「用于还原正常业务流程的时间线」 | 时间线是 tracing 的职责。日志堆不出调用链，只堆得出噪音。 |
| 「反映重复消费率 / 超时率，值得监控告警」 | 频率与趋势是 metrics 的职责。用日志数条数既贵又不准。 |
| 「不接 APM 也能定位慢请求」 | 那就去接 APM。`Stopwatch` 日志是给自己造一个劣质 APM。 |
| 「避免把重试做成黑盒」 | 重试的中间态没有消费者。只有耗尽才需要人介入。 |
| 「生产级代码需要完善的日志」 | 完善 ≠ 多。完善是「出事时有且只有该看的那几条」。 |
| 「先记着，万一以后要排查」 | 「万一」不是消费者。真要排查时加 Debug，别留在生产热路径。 |
| 「这条日志又不费什么性能」 | 成本不在性能，在信噪比。每条无用日志都在降低 Error 被发现的概率。 |

## 危险信号

写完日志后自查，出现以下任一情况，回去删：

- 一个方法里超过 2 条日志
- `foreach` / `for` 循环体内出现 `_logger`
- 日志消息以「开始」「完成」「成功」开头
- 同一个 `try/catch` 里既 `Log*(ex, ...)` 又 `throw`
- 日志模板里出现 `$"` 或 `ex.Message`
- 走通一次正常业务流程，日志超过 1 条
