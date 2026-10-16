---
name: custom-fastendpoints-rule
description: 在编写任何 API 的时候，例如用户要求创建/新增 Endpoint、接口，或者使用 GET|POST|PUT|DELET 语义构造 HTTP API 的时候触发。涵盖了 FastEndpoints 的使用规范、目录布局、命名规范、路由、鉴权、响应、事务、数据访问等规则。
---

# FastEndpoints 设计规范

## 工作流

1. **选域** —— `Management/`（管理端）或 `UserSide/`（用户端）
2. **定 feature 目录** —— 如 `src/Endpoints/Management/Tags/`
3. **检查 ApiTags** —— 在 `src/Endpoints/ApiTags.cs` 找匹配 tag；**找不到就必须新增**一个 const（格式：`【管理端】xxx` / `【用户端】xxx` / `【管理员】xxx`）
4. **建 Request** —— `DTOs/{Action}{Resource}Request.cs`
  - 单 Id 查询/删除 → 实现 `IEntityDto`
  - 列表分页 → 继承 `PagedResultRequestDto`
5. **仅当需要校验时**建 Validator —— `Validators/{Request 类名}Validator.cs`
6. **查询类**建 Response —— `DTOs/{Resource}ResultDto.cs` 或 `{Resource}ListResultDto.cs`，后者是给列表 API 使用的
7. **建 Endpoint**
  - 无响应体 → `templates/Endpoint.cs`
  - 有响应体 → `templates/Endpoint.WithResponse.cs`
8. **完成 `Configure()`** —— HTTP verb + 路由 + 鉴权 + `Description(x => x.WithTags(ApiTags.*))`
9. **写操作加 `[Transactional]`**，取决于是否涉及到超过 1 个业务/聚合根的写操作

## 硬规则
### 继承与构造
- 必须继承下列之一：
  - `BaseEndpoint<TRequest>` —— 有入参、无响应体
  - `BaseEndpoint<TRequest, TResponse>` —— 有入参、有响应体
  - `BaseEndpointWithoutRequest` —— 无入参、无响应体
  - `BaseEndpointWithoutRequest<TResponse>` —— 无入参、有响应体
- 主构造器**第一个参数固定** `IServiceProvider serviceProvider`，业务依赖排其后
- 形式：`public class XxxEndpoint(IServiceProvider serviceProvider, XxxManager manager) : BaseEndpoint<...>(serviceProvider)`

### XML 文档注释（必填）
- Endpoint 类上必须有 `/// <summary>`，以中文前缀开头：`【管理端】` / `【用户端】` / `【管理员】`
- 格式：`【管理端】{Feature} - {Action 描述}`
- **每个 DTO 属性**也要 XML 注释（中文）

### 路由
- 前缀：`/api/{management|user-side}/{feature}/...`
- 多词用 **kebab-case**：`/api/management/card-codes` ✅ `/api/management/cardCodes` ❌
- 路由参数靠属性名匹配（大小写不敏感），**不要**用 `[FromRoute]` / `[BindFrom]`

### 鉴权（三选一）
- `AllowAnonymous()` —— 公开
- `Roles(InternalRoles.Admin, InternalRoles.NormalUser, ...)` —— 角色限定
- 不写 —— 默认已登录

### 数据访问分层（关键）
- **写操作**：只能经 `*Manager`（如 `TagManager.CreateAsync(...)`），但是某些时候对应的业务操作只是单个聚合完成，不需要跨聚合或者借助 Manager 编排，则直接可以使用 IFreeSQL 或者仓储拿取对应的聚合根或者实体进行操作。
- **读操作**：
  - 简单 by-id → `IRepository<T>.FindAsync(id)`
  - 复杂 join/投影 → 允许直接注入并使用 `IFreeSql`

### 事务与响应
- 涉及到超过 1 个业务/聚合根的写操作 `HandleAsync` 必须加 `[Transactional]`
- 业务错误 `throw new BusinessException(...)`，**不要**手写 `Send.BadRequestAsync`

### DTO 命名

| 类型 | 模式 | 示例 |
|---|---|---|
| 入参 | `{Action}{Resource}Request` | `CreateTagRequest` |
| 查询结果 | `{Resource}ResultDto` 或 `{Resource}ListResultDto` | `TagListResultDto` |
| **仅限认证/token 端点** | `{...}Response` | `LoginResponse`、`TokenResponse` |
| Validator | `{Request 类名}Validator` | `CreateTagRequestValidator` |
- 某些简单的请求，比如说查询订单信息的 Endpoint 命名为 `QueryOrderInformationEndpoint`，那么它的查询 DTO 命名就是 `QueryOrderInformationRequest`，而不是 `GetOrderInformationRequest`，以保持与 Endpoint 的一致性
- 简单请求的返回值也是一样的，比如说 `QueryOrderInformationEndpoint` 的返回值 DTO 就命名为 `QueryOrderInformationResponse`，以保持与 Endpoint 的一致性

### DTO 属性
- 必填 string → `string Name { get; set; } = null!;`
- 可选 → `string? Description { get; set; }`
- 所有属性必须有 `/// <summary>` 中文注释，不要单行，使用多行进行。

### DTO 目录结构（二选一）
- **平铺**（feature DTO 少时）：`DTOs/XxxRequest.cs` + `DTOs/XxxResultDto.cs`
- **拆分**（feature DTO 多时）：`DTOs/Requests/XxxRequest.cs` + `DTOs/Responses/XxxResultDto.cs`

### 动词命名
- 标准：`Create / Get / GetList / Update / Delete`
- 领域语义：`Bind / Export / Redirect / Stream / Buy` 等有明确含义时

## 文档联网查询规则
- 在编写代码的时候，遇到不确定的规则或者不确定的写法时，必须联网查询官方文档或者社区文档，确认写法是否正确，是否符合规范。