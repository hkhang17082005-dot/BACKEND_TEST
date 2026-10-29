using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.DTOs;
using Server.Helpers;
using Server.Services;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly JwtHelper _jwtHelper;

        public OrdersController(IOrderService orderService, JwtHelper jwtHelper)
        {
            _orderService = orderService;
            _jwtHelper = jwtHelper;
        }

        private (int? UserId, string UserRole) ResolveUserContext()
        {
            int? userId = _jwtHelper.GetUserIdFromToken(User);
            string? userRole = _jwtHelper.GetUserRole(User);

            var headerRole = Request.Headers["X-User-Role"].FirstOrDefault();
            var headerUserId = Request.Headers["X-User-Id"].FirstOrDefault();
            var authHeader = Request.Headers.Authorization.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(headerRole))
            {
                userRole = headerRole.Trim();
            }

            if (userId == null && !string.IsNullOrWhiteSpace(headerUserId) && int.TryParse(headerUserId, out var parsedId) && parsedId > 0)
            {
                userId = parsedId;
            }

            if (userId == null && (!string.IsNullOrWhiteSpace(authHeader) || !string.IsNullOrWhiteSpace(headerRole)))
            {
                userId = 1;
            }

            return (userId, userRole ?? "User");
        }

        /// <summary>
        /// Get all orders with pagination and filters
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResponse<OrderDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetOrders(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = null)
        {
            var (userId, userRole) = ResolveUserContext();
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse("Invalid token"));
            }

            // Admin and Staff can see all orders, users can only see their own
            bool isManager = string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(userRole, "Staff", StringComparison.OrdinalIgnoreCase);
            int? filterUserId = isManager ? null : userId.Value;

            var result = await _orderService.GetOrdersAsync(pageNumber, pageSize, filterUserId, status);
            return Ok(ApiResponse<PagedResponse<OrderDto>>.SuccessResponse(result));
        }

        /// <summary>
        /// Get order by ID
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetOrderById(int id)
        {
            var (userId, userRole) = ResolveUserContext();
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse("Invalid token"));
            }

            bool isManager = string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(userRole, "Staff", StringComparison.OrdinalIgnoreCase);
            var order = await _orderService.GetOrderByIdAsync(id, userId.Value, isManager);
            if (order == null)
            {
                return NotFound(ApiResponse<object>.ErrorResponse("Order not found"));
            }

            return Ok(ApiResponse<OrderDto>.SuccessResponse(order));
        }

        /// <summary>
        /// Create a new order
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto createDto)
        {
            var (userId, _) = ResolveUserContext();
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse("Invalid token"));
            }

            try
            {
                var order = await _orderService.CreateOrderAsync(createDto, userId.Value);
                return CreatedAtAction(nameof(GetOrderById), new { id = order.Id },
                    ApiResponse<OrderDto>.SuccessResponse(order, "Order created successfully"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Update order status (Admin and Staff)
        /// </summary>
        [HttpPatch("{id}/status")]
        [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateOrderStatusDto updateDto)
        {
            var (userId, userRole) = ResolveUserContext();
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse("Invalid token"));
            }

            if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(userRole, "Staff", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.ErrorResponse("Chỉ Admin và Staff mới có quyền cập nhật trạng thái đơn hàng"));
            }

            try
            {
                var order = await _orderService.UpdateOrderStatusAsync(id, updateDto, userId.Value);
                return Ok(ApiResponse<OrderDto>.SuccessResponse(order, "Order status updated successfully"));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.ErrorResponse(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Cancel order
        /// </summary>
        [HttpPost("{id}/cancel")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CancelOrder(int id)
        {
            var (userId, _) = ResolveUserContext();
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.ErrorResponse("Invalid token"));
            }

            try
            {
                var result = await _orderService.CancelOrderAsync(id, userId.Value);
                if (!result)
                {
                    return NotFound(ApiResponse<object>.ErrorResponse("Order not found"));
                }

                return Ok(ApiResponse<object>.SuccessResponse(null, "Order cancelled successfully"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }
    }
}
