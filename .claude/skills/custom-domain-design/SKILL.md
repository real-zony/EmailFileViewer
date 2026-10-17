---
name: custom-domain-design
description: 在涉及领域层(Domain 层)的设计问题上提供建议和设计指导，涉及到领域建模和相关内容的设计(实体、聚合根、仓储[Repository]、领域服务[Domain Service]、领域事件、Manager 的时候进行使用)。
---

# Domain 设计规范和规则

## 工作流
1. 模块选择，在对应的 `Domain` 文件夹中，选择合适的文件夹或者新建一个目录，遵循 PascalCase 命名，并使用复数形式（如 `Users`、`Resources`）。
2. 产物列表(可能会有一个或者多个):
  - 聚合根: `templates/Entity.cs`（单文件）或者 `templates/Entity.WithRepository.cs`（三合一）
  - 仓储: `templates/Repository.Interface.cs` + `templates/Repository.Implementation.cs`
  - Manager（聚合根生命周期）: `templates/Manager.cs`
  - Service（跨聚合 Domain Service）: `templates/Service.cs`
  - 领域事件和事件处理器: `templates/Event.cs` + `templates/EventHandler.cs`
  - 定时任务: `templates/BackgroundService.cs`
3. 注册 DI，每个模块必须创建 `{Module}/{Module}DomainServiceExtensions.cs` 文件，并在根文件中调用它，这个文件里面包含了该模块的所有领域服务、仓储、事件处理器等的 DI 注册。

## 硬规则
### 目录组织规则
- 技术分层子目录（固定约定）：`Events/` / `EventHandlers/` / `Repositories/` / `BackgroundJobs/`。
- 业务能力子目录（触发条件同时满足）：
  - 子能力有自己的实体 / 仓储 / 事件 / 专属 Handler（≥ 3 个文件）
  - 子能力在语言上可独立命名（如 `PushResource` / `SessionManagement` / `PlayHistories`）
- 子目录命名用业务能力词，禁止使用 `Entities/` / `Services/` / `Commands/` 等技术层词。

### 实体设计规则
- 所有实体必须继承 `FullAuditedEntity`（提供 `Id` / `CreationTime` / `LastModificationTime`），如果不需要则集成子 `Entity` 并根据需要实现 `IHasCreationTime`/`IHasModificationTime` 接口。
- 所有实体必须使用 `[Table(Name = "XxxEntities")]` 特性来指定表名，遵循 PascalCase + 复数形式。
- 实体如果拥有业务约束或者索引约束，应该查询 FreeSQL 的资料，使用类似于 `[Index("IDX_LoginName", "LoginName", true)]` 来添加索引。
- 构造器默认都是一个 `protected` 的构造器，以避免没有业务意义的实体被创建，同时提供一个 `public` 的构造器，参数为业务相关的属性。
- 实体的可见性规则:
  - 影响业务不变量 → `{ get; protected set; }` —— 外部只能通过实体方法改
  - 元数据 / 可选备注 / 弱关联外键 → `{ get; set; }`（如 `Description` / `BatchId` / `ExpirationTime` / `ParentResourceId`）
- 所有属性都应该拥有中文的 XML 注释。
- 业务动作方法应该封装在实体内部（如 `Publish()` / `Redeem()` / `Lock()`），如果违反业务不变量则抛出 `BusinessException`。
- 长度/约束常量：`public const int XxxMaxLength = N;`
  - 这些长度或者约束常量通常可以被其他需要的地方使用，比如说 DTO 的 Validator 里面。
- 常用 FreeSql 特性：
  - `[Column(StringLength = X, IsNullable = false)]`
  - `[Column(DbType = "text")]`
  - `[Column(DbType = "decimal(18, 2)")]`
  - `[Column(IsPrimary = true)]`（复合主键）
  - `[Index("IDX_TableName_Cols", "Col1 ASC, Col2 ASC")]`（第三参数 `true` = 唯一）

### 仓储 Repostory 设计规则
- 仓储接口命名为 `I{Entity}Repository`，继承自 `IBaseRepository<T, Guid>`。
- 仓储实现类继承自 `BaseRepository<T, Guid>`，构造器固定为：
  ```csharp
  public XxxRepository(UnitOfWorkManager uowMgr) : base(uowMgr.Orm)
  {
      uowMgr.Binding(this);
  }
  ```
  原因是为了确保仓储能够正确地绑定到工作单元（Unit of Work）中，以便在事务中正确地管理数据库操作。
- 自定义方法命名规则参考，其他的根据业务进行定义：
  - `FindByXxxAsync` → 单条可空返回
  - `IsExistAsync` / `IsExistByXxxAsync` → 布尔
  - `DeleteByXxxAsync` → 条件删除
  - `GetCountByXxxAsync` → 聚合计数
  - `HasXxxAsync` → 业务型布尔询问
- 仓储方法应该尽量保持简单，主要负责数据访问和持久化，复杂的业务逻辑应该放在领域服务或者实体方法中。

### Manager (聚合根生命周期管理) 设计规则
- 继承 `DomainService(IServiceProvider serviceProvider)`。
- 使用依赖注入以获取所需要的服务。
- Manager 里面涉及到多个聚合根的写操作时候，应该使用 `[Transactional]` 特性来确保事务的一致性。
- 业务错误 `throw new BusinessException("...") { HttpStatusCode = 403 };`
- 发事件统一使用 FastEndpoints 提供的能力: `Mode.WaitForAll`：`await new XxxEvent(...).PublishAsync(Mode.WaitForAll, ct);`
- 子实体是否需要独立 Manager：
  - 子实体**生命周期从属于聚合根** → **不建** Manager，由父 Manager 顺带操作
  - 子实体**有独立写入路径**（独立增删改查）→ **可以**独立 Manager

### 领域事件设计规则
- 领域事件应该放在 `{Module}/Events/` 目录下，事件处理器应该放在 `{Module}/EventHandlers/` 目录下。
- 领域事件基于 FastEndpoints 提供的事件发布机制。
- 事件类的定义 `public class XxxEvent : IEvent`。
- 命名用**过去时**：`CreatedNewUserEvent` / `AddResourceFavoriteEvent` / `DirectTopEvent`。

### 事件处理器(EventHandler)设计规则
- `public class XxxEventHandler : IEventHandler<XxxEvent>`（一个类可实现多个）
- 构造器**只**注 `ILogger<Self>` + `IServiceScopeFactory` —— Scoped 服务进作用域取用，这是由于事件处理器的生命周期并不是瞬时的，是单例的，所以不能直接注入 Scoped 服务 或者 Transient 服务，否则会导致生命周期不匹配的问题。
- `HandleAsync` 写操作加 `[Transactional]`
- 放 `{Module}/EventHandlers/`
- **不需要 DI 手动注册**，FastEndpoints 自动扫描。

### 其他跨聚合服务设计规则
- 基于职责或者业务场景需要，取名可以自由设计，例如 Service / Checker / Generator / Signer 等等，具体根据实际业务需求和语义来定。
- 默认情况下都应该继承自 `DomainService`，以便能够使用领域服务的功能和特性。

### 定时任务设计规则
- 继承 `Microsoft.Extensions.Hosting.BackgroundService`
- 构造器同 EventHandler：`ILogger` + `IServiceScopeFactory`，原因同上。
- 放 `{Module}/BackgroundJobs/`，如果是定时同步任务则放在 `Syncs/` 子目录下。
- **必须**在 `{Module}DomainServiceExtensions` 里 `services.AddHostedService<>();` 注册。

### DI 注册（硬规则）
- **每个模块必建** `{Module}/{Module}DomainServiceExtensions.cs` 文件。
- 领域服务 / 仓储 / 事件处理器等都应该在这个文件里进行 DI 注册，禁止分散在各个文件里注册。
- 生命周期:
  - Manager / Service / Checker / Generator / Signer 等领域服务注册为 Scoped。
  - 无状态的工具类则使用 Singleton。
  - BackgroundService 注册为 HostedService。
  - ICurrentUser 注册为瞬时对象。
- 基于 FastEndpoints 的自动扫描能力或者注册的对象都不需要手动注册，例如事件处理器。
- 新建立的模块需要在 Domain 跟目录的 `RegisterDomainServiceExtensions` 里的注册方法添加注册调用。