using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.DTOs;
using Server.Helpers;
using Server.Services;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly JwtHelper _jwtHelper;

        public AuthController(IAuthService authService, JwtHelper jwtHelper)
        {
            _authService = authService;
            _jwtHelper = jwtHelper;
        }

        /// <summary>
        /// Đăng ký tài khoản mới qua Email
        /// </summary>
        [HttpPost("register")]
        [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            try
            {
                var result = await _authService.RegisterAsync(registerDto);
                return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result, "Đăng ký thành công"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Đăng nhập tài khoản qua Email và Mật khẩu
        /// </summary>
        [HttpPost("login")]
        [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            try
            {
                var result = await _authService.LoginAsync(loginDto);
                return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result, "Đăng nhập thành công"));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Đăng nhập hoặc Đăng ký tài khoản bằng Google
        /// </summary>
        [HttpPost("google")]
        [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthDto googleDto)
        {
            try
            {
                var result = await _authService.GoogleAuthAsync(googleDto);
                var message = result.IsNewUser
                    ? "Đăng ký và đăng nhập bằng Google thành công"
                    : "Đăng nhập bằng Google thành công";
                return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result, message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Kiểm tra thông tin đăng nhập (Email & Mật khẩu) và gửi mã OTP qua Email
        /// </summary>
        [HttpPost("send-login-otp")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SendLoginOtp([FromBody] LoginDto loginDto)
        {
            try
            {
                var sent = await _authService.SendLoginOtpAsync(loginDto);
                if (sent)
                {
                    return Ok(ApiResponse<object>.SuccessResponse(new { email = loginDto.Email }, "Mã OTP đăng nhập đã được gửi đến email của bạn"));
                }
                return BadRequest(ApiResponse<object>.ErrorResponse("Không thể gửi email OTP, vui lòng kiểm tra lại địa chỉ email"));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Gửi mã xác thực OTP qua Email
        /// </summary>
        [HttpPost("send-otp")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpDto sendOtpDto)
        {
            try
            {
                var sent = await _authService.SendOtpAsync(sendOtpDto.Email);
                if (sent)
                {
                    return Ok(ApiResponse<object>.SuccessResponse(new { email = sendOtpDto.Email }, "Mã OTP đã được gửi đến email của bạn"));
                }
                return BadRequest(ApiResponse<object>.ErrorResponse("Không thể gửi email OTP, vui lòng kiểm tra lại địa chỉ email"));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Xác thực mã OTP
        /// </summary>
        [HttpPost("verify-otp")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto verifyOtpDto)
        {
            var isValid = await _authService.VerifyOtpAsync(verifyOtpDto.Email, verifyOtpDto.Otp);
            if (isValid)
            {
                return Ok(ApiResponse<object>.SuccessResponse(new { valid = true }, "Mã OTP hợp lệ"));
            }

            return BadRequest(ApiResponse<object>.ErrorResponse("Mã OTP không chính xác hoặc đã hết hạn"));
        }

        /// <summary>
        /// Lấy thông tin tài khoản hiện tại
        /// </summary>
        [HttpGet("me")]
        [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetCurrentUser()
        {
            var userId = _jwtHelper.GetUserIdFromToken(User);
            if (userId == null)
            {
                var headerUserId = Request.Headers["X-User-Id"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(headerUserId) && int.TryParse(headerUserId, out var parsedId) && parsedId > 0)
                {
                    userId = parsedId;
                }
            }

            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse("Token không hợp lệ"));
            }

            var user = await _authService.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return NotFound(ApiResponse<object>.ErrorResponse("Người dùng không tồn tại"));
            }

            return Ok(ApiResponse<UserDto>.SuccessResponse(user));
        }

        /// <summary>
        /// Lấy danh sách tất cả người dùng (Admin)
        /// </summary>
        [HttpGet("users")]
        [ProducesResponseType(typeof(ApiResponse<List<UserDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllUsers()
        {
            var role = _jwtHelper.GetUserRole(User) ?? Request.Headers["X-User-Role"].FirstOrDefault();
            var authHeader = Request.Headers.Authorization.FirstOrDefault();

            if (User.Identity?.IsAuthenticated != true && string.IsNullOrWhiteSpace(authHeader) && string.IsNullOrWhiteSpace(role))
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse("Vui lòng đăng nhập để thực hiện thao tác này"));
            }

            var users = await _authService.GetAllUsersAsync();
            return Ok(ApiResponse<List<UserDto>>.SuccessResponse(users));
        }

        /// <summary>
        /// Cập nhật vai trò (ADMIN, STAFF, USER) cho người dùng
        /// </summary>
        [HttpPut("users/{id}/role")]
        [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] UpdateUserRoleDto dto)
        {
            var role = _jwtHelper.GetUserRole(User) ?? Request.Headers["X-User-Role"].FirstOrDefault();
            var authHeader = Request.Headers.Authorization.FirstOrDefault();

            if (User.Identity?.IsAuthenticated != true && string.IsNullOrWhiteSpace(authHeader) && string.IsNullOrWhiteSpace(role))
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse("Vui lòng đăng nhập để thực hiện thao tác này"));
            }

            try
            {
                var updatedUser = await _authService.UpdateUserRoleAsync(id, dto.Role);
                return Ok(ApiResponse<UserDto>.SuccessResponse(updatedUser, $"Đã cập nhật Role thành {updatedUser.Role}"));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }
    }
}
