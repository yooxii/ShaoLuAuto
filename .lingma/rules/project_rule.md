
# C# WPF 项目规范 (Clean Architecture 实现)

## 技术栈
- **语言/框架**：C# 8+ (.NET Framework 4.8), WPF
- **架构模式**：Clean Architecture + MVVM
- **核心库**：
  - Prism 7.x (MVVM/导航，需兼容 .NET 4.8)
  - **Entity Framework 6.x** (数据访问，替代 EF Core)
  - Serilog (结构化日志)
  - **System.Text.Json** (需通过 NuGet 安装，兼容 .NET 4.8)
  - **WPFLocalizeExtension** (多语言支持)
- **安全**：`System.Security.Cryptography` (数据保护)
- **测试**：xUnit 2.4+ (需适配 .NET 4.8), Moq 4.18+

---

## 分层规范
### 1. Domain Layer (核心领域)
- **职责**：业务规则、实体定义、领域异常
- **强制规则**：
  - 零外部依赖（禁止引用 `System.Windows`/`Microsoft.EntityFrameworkCore`）
  - 实体方法封装业务逻辑（禁止 `public set`）
  - 领域异常继承 `DomainException`

```csharp
// src/MyApp.Core/Entities/User.cs
public class User
{
    public Guid Id { get; private init; } // 注意：.NET 4.8 支持 C# 8 特性（如 `init`）
    public string Name { get; private set; }
    public bool IsActive { get; private set; }

    // 业务规则：激活用户
    public void Activate()
    {
        if (IsActive) 
            throw new DomainException("User already active", 4001);
        IsActive = true;
    }
}

// src/MyApp.Core/Exceptions/DomainException.cs
public class DomainException : Exception
{
    public int ErrorCode { get; }
    public DomainException(string message, int errorCode = 400) 
        : base(message) => ErrorCode = errorCode;
}
```

### 2. Application Layer (应用服务)

- **职责**：用例协调、事务边界、权限控制
- **强制规则**：
    - 接口定义在 `Core` 层，实现在 `Application` 层
    - 所有方法接收 `CancellationToken`（需通过 `System.Threading` 引用）
    - **禁止直接操作 UI**

```csharp
// src/MyApp.Core/Services/IUserService.cs
public interface IUserService
{
    Task<IEnumerable<User>> GetActiveUsersAsync(CancellationToken ct = default);
}

// src/MyApp.Application/Services/UserService.cs
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    
    public UserService(IUserRepository userRepository) 
        => _userRepository = userRepository;
    
    public async Task<IEnumerable<User>> GetActiveUsersAsync(CancellationToken ct)
    {
        return await _userRepository.GetActiveUsersAsync(ct);
    }
}
```

### 3. Infrastructure Layer (基础设施)

- **职责**：数据访问、外部服务集成
- **强制规则**：
    - 实现 `Core` 层定义的仓储接口
    - **仅使用参数化查询**（防 SQL 注入）
    - EF 6 映射配置分离到 `DbContext`

```csharp
// src/MyApp.Infrastructure/Repositories/UserRepository.cs
public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;
    
    public UserRepository(AppDbContext context) 
        => _context = context;
    
    public async Task<IEnumerable<User>> GetActiveUsersAsync(CancellationToken ct)
    {
        // EF 6 参数化查询（安全）
        return await _context.Users
            .Where(u => u.IsActive) // 推荐使用 LINQ 表达式
            .ToListAsync(ct);
    }
}

// src/MyApp.Infrastructure/Data/AppDbContext.cs
public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; } // EF 6 映射
    
    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().ToTable("Users");
    }
}
```

### 4. Presentation Layer (UI 层)

- **职责**：UI 交互、数据绑定
- **强制规则**：
    - View (XAML) **仅包含 UI 定义**，无代码后台
    - ViewModel 通过 `ICommand` 调用 Application 服务
    - **所有 UI 更新必须通过 Dispatcher**

```csharp
// src/MyApp.Presentation/ViewModels/UserViewModel.cs
public partial class UserViewModel : ObservableObject
{
    private readonly IUserService _userService;
    
    [ObservableProperty]
    private ObservableCollection<UserModel> _users = new();
    
    public UserViewModel(IUserService userService)
    {
        _userService = userService;
        LoadUsersCommand = new DelegateCommand(LoadUsersAsync); // Prism 7 的 `DelegateCommand`
    }

    public DelegateCommand LoadUsersCommand { get; }
    
    private async Task LoadUsersAsync()
    {
        try {
            var users = await _userService.GetActiveUsersAsync();
            // 线程安全的 UI 更新
            await Application.Current.Dispatcher.InvokeAsync(() => {
                Users = new ObservableCollection<UserModel>(
                    users.Select(u => new UserModel(u.Id, u.Name))
                );
            });
        }
        catch (DomainException ex) {
            // 错误传递给 View
            await Application.Current.Dispatcher.InvokeAsync(() => 
                ShowError(ex.Message)
            );
        }
    }
}
```

---

## 专项规范

### 并发安全

- **UI 线程保护**：所有 UI 操作必须包装在 `Dispatcher.InvokeAsync`
- **异步操作**：使用 `DelegateCommand`（Prism 7），禁止 `Task.Run` 阻塞 UI
- **共享数据**：使用 `lock` 或 `SemaphoreSlim` 保护静态资源

### 安全规范

- **敏感数据**：使用 `ProtectedData` 加密本地存储
- **反序列化**：禁用 `XmlSerializer`，使用 `DataContractSerializer` + 严格类型验证
- **输入验证**：ViewModel 属性使用 `System.ComponentModel.DataAnnotations`

### 错误处理

- **全局异常**：在 `App.xaml.cs` 捕获未处理异常
- **ViewModel 错误**：通过 `ICommand` 的 `Exception` 事件通知 UI

### 依赖注入

- **注册规则**：
- **依赖方向**：`Presentation → Application → Domain`，`Infrastructure → Application → Domain`

### 多语言支持

- **字符串源**：使用 `ResourceManager`:

```csharp
var title = LocalizeDictionary.Instance.GetLocalizedObject("Select_target_pic", null, null)?.ToString() ?? "Open Image File";
```
- **资源文件**：使用 `.resx` 文件

---

## 项目结构

```bash
MyApp/
├── src/
│   ├── MyApp.Core/               # Domain Layer
│   │   ├── Entities/             # 领域实体
│   │   ├── Services/             # 服务接口
│   │   └── Exceptions/           # 领域异常
│   │
│   ├── MyApp.Application/        # Application Layer
│   │   └── Services/             # 服务实现
│   │
│   ├── MyApp.Infrastructure/     # Infrastructure Layer
│   │   ├── Repositories/
│   │   └── Data/                 # DbContext 配置
│   │
│   └── MyApp.Presentation/       # Presentation Layer (WPF)
```

---

## 注意事项

1. **.NET Framework 4.8 安装**：
    - 下载链接：微软官方下载页面
    - 安装后需重启系统生效。
2. **NuGet 依赖**：
    - 在 `MyApp.Infrastructure` 项目中安装 `EntityFramework`（版本适配 .NET 4.8）。
    - 在 `MyApp.Core` 和 `MyApp.Presentation` 项目中安装 `System.Text.Json`（需手动引用 NuGet 包）。
3. **兼容性检查**：
    - 确认 Prism 版本支持 .NET 4.8（例如 Prism 7.2）。
    - 确保所有第三方库的 `Target Framework` 设置为 `.NET Framework 4.8`。

---

**修改说明**：

1. **技术栈更新**：
    - 替换 `.NET 6+` 为 `.NET Framework 4.8`，并调整相关库（EF Core → EF 6）。
    - 明确 C# 版本支持（建议 C# 8，兼容 .NET 4.8）。
2. **代码适配**：
    - EF 6 的 `DbContext` 和查询语法替换。
    - Prism 命令改为 `DelegateCommand`（兼容 .NET 4.8）。
3. **注意事项补充**：
    - 添加 .NET 4.8 安装说明和 NuGet 依赖提示。

