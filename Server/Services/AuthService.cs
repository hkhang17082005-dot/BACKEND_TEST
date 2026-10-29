using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Server.Data;
using Server.DTOs;
using Server.Helpers;
using Server.Models;

namespace Server.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto);
        Task<AuthResponseDto> LoginAsync(LoginDto loginDto);
        Task<AuthResponseDto> GoogleAuthAsync(GoogleAuthDto googleDto);
        Task<bool> SendLoginOtpAsync(LoginDto loginDto);
        Task<bool> SendOtpAsync(string email);
        Task<bool> VerifyOtpAsync(string email, string otp);
        Task<UserDto?> GetUserByIdAsync(int userId);
        Task<List<UserDto>> GetAllUsersAsync();
        Task<UserDto> UpdateUserRoleAsync(int userId, string role);
    }

    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly JwtHelper _jwtHelper;
        private readonly IEmailService _emailService;
        private readonly IMemoryCache _memoryCache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            ApplicationDbContext context,
            JwtHelper jwtHelper,
            IEmailService emailService,
            IMemoryCache memoryCache,
            IConfiguration configuration,
            ILogger<AuthService> logger)
        {
            _context = context;
            _jwtHelper = jwtHelper;
            _emailService = emailService;
            _memoryCache = memoryCache;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto)
        {
            var normalizedEmail = registerDto.Email.Trim().ToLower();

            // Check if email already exists
            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            {
                throw new InvalidOperationException("Email đã được sử dụng");
            }

            // Verify OTP (bắt buộc khi đăng ký)
            if (string.IsNullOrWhiteSpace(registerDto.Otp))
            {
                throw new InvalidOperationException("Vui lòng nhập mã xác thực OTP đã được gửi đến email của bạn");
            }

            var cacheKey = $"otp_{normalizedEmail}";
            if (!_memoryCache.TryGetValue(cacheKey, out string? storedOtp) || storedOtp != registerDto.Otp.Trim())
            {
                throw new InvalidOperationException("Mã OTP không chính xác hoặc đã hết hạn");
            }
            _memoryCache.Remove(cacheKey);

            // Create new user
            var user = new User
            {
                Email = normalizedEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(registerDto.Password),
                FullName = registerDto.FullName.Trim(),
                PhoneNumber = string.IsNullOrWhiteSpace(registerDto.PhoneNumber) ? null : registerDto.PhoneNumber.Trim(),
                Role = UserRole.User,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Send welcome email via Resend
            try
            {
                await _emailService.SendWelcomeEmailAsync(user.Email, user.FullName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi email chào mừng tới {Email}", user.Email);
            }

            // Generate token
            var token = _jwtHelper.GenerateToken(user);

            return new AuthResponseDto
            {
                Token = token,
                User = MapToUserDto(user)
            };
        }

        private async Task EnsureDefaultAccountsAsync()
        {
            var defaultAccounts = new[]
            {
                (Email: "anhjkr@gmail.com", LegacyUsername: "anhjkr", FullName: "Hồng Hoàng Khang (User)", Role: UserRole.User),
                (Email: "anhjkrr@gmail.com", LegacyUsername: "anhjkrr", FullName: "Hồng Hoàng Khang (Staff)", Role: UserRole.Staff),
                (Email: "anhjkrrr@gmail.com", LegacyUsername: "anhjkrrr", FullName: "Hồng Hoàng Khang (Admin)", Role: UserRole.Admin),
                (Email: "honghoangkhang@gmail.com", LegacyUsername: "honghoangkhang", FullName: "Hồng Hoàng Khang", Role: UserRole.User),
                (Email: "honghoangkhang.staff@gmail.com", LegacyUsername: "khangstaff", FullName: "Hồng Hoàng Khang (Staff)", Role: UserRole.Staff),
                (Email: "honghoangkhang.admin@gmail.com", LegacyUsername: "khangadmin", FullName: "Hồng Hoàng Khang (Admin)", Role: UserRole.Admin),
            };

            bool changed = false;
            foreach (var acc in defaultAccounts)
            {
                var existingByEmail = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == acc.Email);
                var existingLegacy = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == acc.LegacyUsername);

                if (existingByEmail == null && existingLegacy != null)
                {
                    existingLegacy.Email = acc.Email;
                    existingByEmail = existingLegacy;
                    changed = true;
                }
                else if (existingByEmail != null && existingLegacy != null && existingByEmail.Id != existingLegacy.Id)
                {
                    _context.Users.Remove(existingLegacy);
                    changed = true;
                }

                if (existingByEmail == null)
                {
                    _context.Users.Add(new User
                    {
                        Email = acc.Email,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Anhhung1@"),
                        FullName = acc.FullName,
                        PhoneNumber = "0901234567",
                        Role = acc.Role,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    });
                    changed = true;
                }
                else
                {
                    bool userUpdated = false;
                    if (existingByEmail.Email != acc.Email || existingByEmail.FullName != acc.FullName)
                    {
                        existingByEmail.Email = acc.Email; existingByEmail.FullName = acc.FullName;
                        userUpdated = true;
                    }
                    if (existingByEmail.Role != acc.Role)
                    {
                        existingByEmail.Role = acc.Role;
                        userUpdated = true;
                    }
                    if (!existingByEmail.IsActive)
                    {
                        existingByEmail.IsActive = true;
                        userUpdated = true;
                    }
                    if (!BCrypt.Net.BCrypt.Verify("Anhhung1@", existingByEmail.PasswordHash))
                    {
                        existingByEmail.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Anhhung1@");
                        userUpdated = true;
                    }
                    if (userUpdated)
                    {
                        existingByEmail.UpdatedAt = DateTime.UtcNow;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                await _context.SaveChangesAsync();
            }
        }

        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
        {
            await EnsureDefaultAccountsAsync();

            var normalizedEmail = loginDto.Email.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(normalizedEmail) || !normalizedEmail.Contains('@'))
            {
                throw new UnauthorizedAccessException("Vui lòng đăng nhập bằng địa chỉ Email hợp lệ");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
            {
                throw new UnauthorizedAccessException("Email hoặc mật khẩu không chính xác");
            }

            if (!user.IsActive)
            {
                throw new UnauthorizedAccessException("Tài khoản đã bị vô hiệu hóa");
            }

            // Bắt buộc xác thực OTP khi đăng nhập
            if (string.IsNullOrWhiteSpace(loginDto.Otp))
            {
                throw new UnauthorizedAccessException("Vui lòng nhập mã xác thực OTP đã được gửi đến email của bạn");
            }

            var cacheKey = $"otp_{normalizedEmail}";
            if (!_memoryCache.TryGetValue(cacheKey, out string? storedOtp) || storedOtp != loginDto.Otp.Trim())
            {
                throw new UnauthorizedAccessException("Mã OTP không chính xác hoặc đã hết hạn");
            }
            _memoryCache.Remove(cacheKey);

            // Đảm bảo Role hợp lệ trong 3 Role (User, Staff, Admin), mặc định là User
            if (!Enum.IsDefined(typeof(UserRole), user.Role))
            {
                user.Role = UserRole.User;
                await _context.SaveChangesAsync();
            }

            // Generate token
            var token = _jwtHelper.GenerateToken(user);

            return new AuthResponseDto
            {
                Token = token,
                User = MapToUserDto(user)
            };
        }

        public async Task<AuthResponseDto> GoogleAuthAsync(GoogleAuthDto googleDto)
        {
            string? email = googleDto.Email;
            string? fullName = googleDto.FullName;

            // Xác thực Google ID Token nếu client gửi lên
            if (!string.IsNullOrWhiteSpace(googleDto.IdToken))
            {
                try
                {
                    var clientId = _configuration["Google:ClientId"];
                    GoogleJsonWebSignature.Payload payload;

                    if (!string.IsNullOrWhiteSpace(clientId))
                    {
                        var settings = new GoogleJsonWebSignature.ValidationSettings
                        {
                            Audience = new[] { clientId }
                        };
                        payload = await GoogleJsonWebSignature.ValidateAsync(googleDto.IdToken, settings);
                    }
                    else
                    {
                        payload = await GoogleJsonWebSignature.ValidateAsync(googleDto.IdToken);
                    }

                    email = payload.Email;
                    if (!string.IsNullOrWhiteSpace(payload.Name))
                    {
                        fullName = payload.Name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể xác thực Google ID Token trực tiếp, kiểm tra thông tin fallback từ Google SDK.");
                    if (string.IsNullOrWhiteSpace(email))
                    {
                        throw new UnauthorizedAccessException("Google ID Token không hợp lệ hoặc đã hết hạn");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("Không tìm thấy địa chỉ Email từ tài khoản Google");
            }

            var normalizedEmail = email.Trim().ToLower();
            var resolvedName = !string.IsNullOrWhiteSpace(fullName)
                ? fullName.Trim()
                : normalizedEmail.Split('@')[0];

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            bool isNewUser = false;

            if (user == null)
            {
                // Tự động đăng ký tài khoản mới khi người dùng đăng nhập Google lần đầu
                isNewUser = true;
                user = new User
                {
                    Email = normalizedEmail,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
                    FullName = resolvedName,
                    Role = UserRole.User,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                try
                {
                    await _emailService.SendWelcomeEmailAsync(user.Email, user.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi gửi email chào mừng tới {Email}", user.Email);
                }
            }
            else
            {
                if (!user.IsActive)
                {
                    throw new UnauthorizedAccessException("Tài khoản đã bị vô hiệu hóa");
                }

                bool updated = false;
                if (string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(resolvedName))
                {
                    user.FullName = resolvedName;
                    updated = true;
                }

                if (!Enum.IsDefined(typeof(UserRole), user.Role))
                {
                    user.Role = UserRole.User;
                    updated = true;
                }

                if (updated)
                {
                    user.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            var token = _jwtHelper.GenerateToken(user);

            return new AuthResponseDto
            {
                Token = token,
                User = MapToUserDto(user),
                IsNewUser = isNewUser
            };
        }

        public async Task<bool> SendLoginOtpAsync(LoginDto loginDto)
        {
            await EnsureDefaultAccountsAsync();

            var normalizedEmail = loginDto.Email.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(normalizedEmail) || !normalizedEmail.Contains('@'))
            {
                throw new UnauthorizedAccessException("Vui lòng nhập địa chỉ Email hợp lệ");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
            {
                throw new UnauthorizedAccessException("Email hoặc mật khẩu không chính xác");
            }

            if (!user.IsActive)
            {
                throw new UnauthorizedAccessException("Tài khoản đã bị vô hiệu hóa");
            }

            return await SendOtpAsync(normalizedEmail);
        }

        public async Task<bool> SendOtpAsync(string email)
        {
            var normalizedEmail = email.Trim().ToLower();
            var otp = Random.Shared.Next(100000, 999999).ToString();
            var cacheKey = $"otp_{normalizedEmail}";

            _memoryCache.Set(cacheKey, otp, TimeSpan.FromMinutes(5));
            _logger.LogInformation("Đã tạo mã OTP cho {Email}: {Otp} (hiệu lực 5 phút)", normalizedEmail, otp);

            return await _emailService.SendOtpEmailAsync(normalizedEmail, otp);
        }

        public Task<bool> VerifyOtpAsync(string email, string otp)
        {
            var normalizedEmail = email.Trim().ToLower();
            var cacheKey = $"otp_{normalizedEmail}";

            if (_memoryCache.TryGetValue(cacheKey, out string? storedOtp) && storedOtp == otp.Trim())
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public async Task<UserDto?> GetUserByIdAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            return user != null ? MapToUserDto(user) : null;
        }

        public async Task<List<UserDto>> GetAllUsersAsync()
        {
            await EnsureDefaultAccountsAsync();
            var users = await _context.Users
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();
            return users.Select(MapToUserDto).ToList();
        }

        public async Task<UserDto> UpdateUserRoleAsync(int userId, string role)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException("Người dùng không tồn tại");
            }

            if (!Enum.TryParse<UserRole>(role.Trim(), true, out var parsedRole))
            {
                parsedRole = UserRole.User;
            }

            user.Role = parsedRole;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var dto = MapToUserDto(user);
            dto.Token = _jwtHelper.GenerateToken(user);
            return dto;
        }

        private static UserDto MapToUserDto(User user)
        {
            var roleStr = Enum.IsDefined(typeof(UserRole), user.Role)
                ? user.Role.ToString()
                : UserRole.User.ToString();

            return new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Role = roleStr,
                CreatedAt = user.CreatedAt
            };
        }
    }
}
