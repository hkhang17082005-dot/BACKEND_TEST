using System.Collections.Concurrent;
using System.Globalization;
using Server.DTOs;

namespace Server.Services
{
    public class LocationService : ILocationService
    {
        private readonly ConcurrentDictionary<string, SharedLocationDto> _sharedLocations = new();

        public LocationService()
        {
            SeedDefaultSampleLocations();
        }

        private void SeedDefaultSampleLocations()
        {
            var now = DateTime.UtcNow;
            var sample = new SharedLocationDto
            {
                Id = "loc-seed-1",
                ShareCode = "LOC-USER-8821",
                UserId = 1,
                UserName = "Hồng Hoàng Khang (User)",
                UserRole = "User",
                Latitude = 10.776889,
                Longitude = 106.700981,
                AccuracyMeters = 3.4,
                AltitudeMeters = 12.5,
                SpeedMps = 0.0,
                BearingDegrees = 90.0,
                Provider = "GPS_SATELLITE",
                Address = "Đường Đồng Khởi, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
                Ward = "Phường Bến Nghé",
                District = "Quận 1",
                City = "TP. Hồ Chí Minh",
                PlusCode = EncodePlusCode(10.776889, 106.700981),
                DmsCoordinates = FormatDms(10.776889, 106.700981),
                GoogleMapsUrl = BuildGoogleMapsUrl(10.776889, 106.700981),
                ShareType = "pickup",
                RecipientTarget = "Gia đình & Bé Minh An",
                Note = "Ba đang đứng chờ đón con tại điểm hẹn cổng chính",
                IsLive = false,
                IsActive = false,
                BatteryLevel = 92,
                CreatedAt = now.AddMinutes(-35),
                UpdatedAt = now.AddMinutes(-35),
                ExpiresAt = now.AddMinutes(-5)
            };

            _sharedLocations[sample.Id] = sample;
        }

        public Task<SharedLocationDto> ShareLocationAsync(
            int userId,
            string userName,
            string userRole,
            ShareLocationRequestDto dto)
        {
            ValidateCoordinates(dto.Latitude, dto.Longitude);

            var now = DateTime.UtcNow;

            // Nếu bật Live Share mới, tự động dừng phiên Live cũ của cùng User
            if (dto.IsLive)
            {
                foreach (var existing in _sharedLocations.Values.Where(x => x.UserId == userId && x.IsLive && x.IsActive))
                {
                    existing.IsActive = false;
                    existing.UpdatedAt = now;
                }
            }

            var id = $"loc-{Guid.NewGuid():N}"[..16];
            var shareCode = $"GPS-{Random.Shared.Next(100000, 999999)}";
            var lat = Math.Round(dto.Latitude, 7);
            var lng = Math.Round(dto.Longitude, 7);
            var accuracy = dto.AccuracyMeters > 0 ? Math.Round(dto.AccuracyMeters, 2) : 4.5;
            var duration = Math.Clamp(dto.DurationMinutes <= 0 ? 30 : dto.DurationMinutes, 5, 1440);

            var address = string.IsNullOrWhiteSpace(dto.Address)
                ? $"Tọa độ chuẩn GPS ({lat.ToString("F6", CultureInfo.InvariantCulture)}, {lng.ToString("F6", CultureInfo.InvariantCulture)})"
                : dto.Address.Trim();

            var shared = new SharedLocationDto
            {
                Id = id,
                ShareCode = shareCode,
                UserId = userId,
                UserName = userName,
                UserRole = userRole,
                Latitude = lat,
                Longitude = lng,
                AccuracyMeters = accuracy,
                AltitudeMeters = Math.Round(dto.AltitudeMeters, 1),
                SpeedMps = Math.Max(0, Math.Round(dto.SpeedMps, 2)),
                BearingDegrees = ((dto.BearingDegrees % 360) + 360) % 360,
                Provider = string.IsNullOrWhiteSpace(dto.Provider) ? "GPS_SATELLITE" : dto.Provider.Trim(),
                Address = address,
                Ward = dto.Ward,
                District = dto.District,
                City = dto.City,
                PlusCode = !string.IsNullOrWhiteSpace(dto.PlusCode) ? dto.PlusCode! : EncodePlusCode(lat, lng),
                DmsCoordinates = FormatDms(lat, lng),
                GoogleMapsUrl = BuildGoogleMapsUrl(lat, lng),
                ShareType = string.IsNullOrWhiteSpace(dto.ShareType) ? "current" : dto.ShareType.Trim(),
                RecipientTarget = string.IsNullOrWhiteSpace(dto.RecipientTarget) ? "Gia đình" : dto.RecipientTarget.Trim(),
                Note = string.IsNullOrWhiteSpace(dto.Note)
                    ? GetDefaultNoteForShareType(dto.ShareType)
                    : dto.Note.Trim(),
                IsLive = dto.IsLive,
                IsActive = dto.IsLive,
                BatteryLevel = Math.Clamp(dto.BatteryLevel, 1, 100),
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = dto.IsLive ? now.AddMinutes(duration) : null
            };

            _sharedLocations[id] = shared;
            return Task.FromResult(shared);
        }

        public Task<SharedLocationDto?> UpdateLiveLocationAsync(
            int userId,
            UpdateLiveLocationDto dto)
        {
            ValidateCoordinates(dto.Latitude, dto.Longitude);

            var now = DateTime.UtcNow;
            var target = _sharedLocations.Values
                .Where(x => x.UserId == userId && x.IsLive && x.IsActive)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefault(x => string.IsNullOrWhiteSpace(dto.ShareCode) ||
                                     string.Equals(x.ShareCode, dto.ShareCode, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                return Task.FromResult<SharedLocationDto?>(null);
            }

            var lat = Math.Round(dto.Latitude, 7);
            var lng = Math.Round(dto.Longitude, 7);

            target.Latitude = lat;
            target.Longitude = lng;
            target.AccuracyMeters = dto.AccuracyMeters > 0 ? Math.Round(dto.AccuracyMeters, 2) : target.AccuracyMeters;
            target.AltitudeMeters = Math.Round(dto.AltitudeMeters, 1);
            target.SpeedMps = Math.Max(0, Math.Round(dto.SpeedMps, 2));
            target.BearingDegrees = ((dto.BearingDegrees % 360) + 360) % 360;
            if (!string.IsNullOrWhiteSpace(dto.Provider))
            {
                target.Provider = dto.Provider.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.Address))
            {
                target.Address = dto.Address.Trim();
            }
            target.PlusCode = EncodePlusCode(lat, lng);
            target.DmsCoordinates = FormatDms(lat, lng);
            target.GoogleMapsUrl = BuildGoogleMapsUrl(lat, lng);
            target.BatteryLevel = Math.Clamp(dto.BatteryLevel, 1, 100);
            target.UpdatedAt = now;

            return Task.FromResult<SharedLocationDto?>(target);
        }

        public Task<bool> StopLiveLocationAsync(int userId, string? shareCode = null)
        {
            var now = DateTime.UtcNow;
            var activeShares = _sharedLocations.Values
                .Where(x => x.UserId == userId && x.IsLive && x.IsActive)
                .Where(x => string.IsNullOrWhiteSpace(shareCode) ||
                            string.Equals(x.ShareCode, shareCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (activeShares.Count == 0)
            {
                return Task.FromResult(false);
            }

            foreach (var item in activeShares)
            {
                item.IsActive = false;
                item.UpdatedAt = now;
            }

            return Task.FromResult(true);
        }

        public Task<UserLocationDashboardDto> GetUserDashboardAsync(
            int userId,
            double? currentLat = null,
            double? currentLng = null)
        {
            var now = DateTime.UtcNow;

            // Tự động hết hạn các phiên Live quá giờ
            foreach (var item in _sharedLocations.Values.Where(x => x.IsLive && x.IsActive && x.ExpiresAt.HasValue && x.ExpiresAt.Value < now))
            {
                item.IsActive = false;
            }

            var userShares = _sharedLocations.Values
                .Where(x => x.UserId == userId || x.UserId == 1)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(20)
                .ToList();

            var activeLive = userShares.FirstOrDefault(x => x.IsLive && x.IsActive);

            double refLat = currentLat ?? activeLive?.Latitude ?? 10.776889;
            double refLng = currentLng ?? activeLive?.Longitude ?? 106.700981;

            // Tọa độ thiết bị của các bé gắn với tài khoản phụ huynh (User)
            // Nếu User đang ở khu vực khác (ví dụ Hà Nội, Đà Nẵng, hoặc Emulator), tự động đặt tọa độ mô phỏng của thiết bị bé gần khu vực phụ huynh để tính khoảng cách thực tế trực quan
            bool isNearHcm = Math.Abs(refLat - 10.776889) < 0.5 && Math.Abs(refLng - 106.700981) < 0.5;
            double kid1Lat = isNearHcm ? 10.779150 : Math.Round(refLat + 0.0024, 7);
            double kid1Lng = isNearHcm ? 106.699420 : Math.Round(refLng - 0.0018, 7);
            double kid2Lat = isNearHcm ? 10.776520 : Math.Round(refLat - 0.0006, 7);
            double kid2Lng = isNearHcm ? 106.701410 : Math.Round(refLng + 0.0005, 7);

            var kids = new List<KidDeviceLocationDto>
            {
                new KidDeviceLocationDto
                {
                    KidId = "kid-minh-an",
                    KidName = "Bé Minh An (7 tuổi)",
                    DeviceName = "Đồng Hồ Định Vị Trẻ Em K2 Pro",
                    Latitude = kid1Lat,
                    Longitude = kid1Lng,
                    AccuracyMeters = 3.8,
                    Address = "Trường Tiểu học Chu Văn An (Trong vùng an toàn)",
                    SafeZoneName = "Trường Tiểu học Chu Văn An",
                    SafeRadiusMeters = 150,
                    IsInSafeZone = true,
                    BatteryPercent = 86,
                    DistanceFromUserMeters = Math.Round(CalculateHaversineMeters(refLat, refLng, kid1Lat, kid1Lng), 1),
                    BearingFromUserText = CalculateBearingDirection(refLat, refLng, kid1Lat, kid1Lng),
                    UpdatedAt = now.AddSeconds(-18)
                },
                new KidDeviceLocationDto
                {
                    KidId = "kid-bong-nhi",
                    KidName = "Bé Bông Nhi (4 tuổi)",
                    DeviceName = "Thiết Bị Định Vị & Giấc Ngủ SafeSleep",
                    Latitude = kid2Lat,
                    Longitude = kid2Lng,
                    AccuracyMeters = 2.5,
                    Address = "Nhà riêng Gia đình (Tầng 2 - An toàn)",
                    SafeZoneName = "Nhà riêng Gia đình",
                    SafeRadiusMeters = 100,
                    IsInSafeZone = true,
                    BatteryPercent = 100,
                    DistanceFromUserMeters = Math.Round(CalculateHaversineMeters(refLat, refLng, kid2Lat, kid2Lng), 1),
                    BearingFromUserText = CalculateBearingDirection(refLat, refLng, kid2Lat, kid2Lng),
                    UpdatedAt = now.AddSeconds(-8)
                }
            };

            return Task.FromResult(new UserLocationDashboardDto
            {
                ActiveLiveShare = activeLive,
                RecentShares = userShares,
                KidLocations = kids
            });
        }

        public Task<SharedLocationDto?> GetSharedLocationByCodeAsync(string shareCode)
        {
            var found = _sharedLocations.Values.FirstOrDefault(x =>
                string.Equals(x.ShareCode, shareCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Id, shareCode, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(found);
        }

        public Task<bool> DeleteSharedLocationAsync(int userId, string id)
        {
            if (_sharedLocations.TryGetValue(id, out var existing))
            {
                if (existing.UserId == userId || existing.UserId == 1)
                {
                    return Task.FromResult(_sharedLocations.TryRemove(id, out _));
                }
            }
            return Task.FromResult(false);
        }

        private static void ValidateCoordinates(double lat, double lng)
        {
            if (double.IsNaN(lat) || double.IsInfinity(lat) || lat < -90 || lat > 90)
                throw new ArgumentException("Vĩ độ (Latitude) không hợp lệ. Phải nằm trong khoảng -90 đến 90 độ.");
            if (double.IsNaN(lng) || double.IsInfinity(lng) || lng < -180 || lng > 180)
                throw new ArgumentException("Kinh độ (Longitude) không hợp lệ. Phải nằm trong khoảng -180 đến 180 độ.");
        }

        private static string GetDefaultNoteForShareType(string? shareType)
        {
            return (shareType?.ToLowerInvariant()) switch
            {
                "live" => "Đang chia sẻ hành trình di chuyển trực tiếp (Live GPS)",
                "pickup" => "Phụ huynh đang ở điểm đón bé",
                "sos" => "CẢNH BÁO KHẨN CẤP (SOS) - Cần hỗ trợ tại vị trí này!",
                "safe_point" => "Điểm hẹn an toàn của gia đình",
                _ => "Chia sẻ vị trí hiện tại của Phụ huynh (User)"
            };
        }

        public static string BuildGoogleMapsUrl(double lat, double lng)
        {
            var latStr = lat.ToString("F7", CultureInfo.InvariantCulture);
            var lngStr = lng.ToString("F7", CultureInfo.InvariantCulture);
            return $"https://www.google.com/maps/search/?api=1&query={latStr},{lngStr}";
        }

        public static string FormatDms(double lat, double lng)
        {
            string ToDms(double coord, string posHemisphere, string negHemisphere)
            {
                var hemisphere = coord >= 0 ? posHemisphere : negHemisphere;
                var abs = Math.Abs(coord);
                var degrees = (int)Math.Floor(abs);
                var minutesFull = (abs - degrees) * 60.0;
                var minutes = (int)Math.Floor(minutesFull);
                var seconds = (minutesFull - minutes) * 60.0;
                return $"{degrees}°{minutes:D2}'{seconds.ToString("00.00", CultureInfo.InvariantCulture)}\"{hemisphere}";
            }

            return $"{ToDms(lat, "N", "S")} {ToDms(lng, "E", "W")}";
        }

        /// <summary>
        /// Mã hóa tọa độ sang Open Location Code (Google Plus Code 10 ký tự độ chính xác ~14m x 14m)
        /// </summary>
        public static string EncodePlusCode(double latitude, double longitude)
        {
            const string alphabet = "23456789CFGHJMPQRVWX";
            double lat = Math.Clamp(latitude, -90.0, 90.0);
            if (lat == 90.0) lat -= 0.00005;
            double lng = longitude;
            while (lng < -180.0) lng += 360.0;
            while (lng >= 180.0) lng -= 360.0;

            lat += 90.0;
            lng += 180.0;

            long latVal = (long)Math.Floor(lat * 8000);
            long lngVal = (long)Math.Floor(lng * 8000);

            char[] code = new char[11];
            for (int i = 4; i >= 0; i--)
            {
                int lngDigit = (int)(lngVal % 20);
                int latDigit = (int)(latVal % 20);
                lngVal /= 20;
                latVal /= 20;

                int pos = i * 2;
                if (pos >= 8) pos++; // Dấu '+' ở vị trí index 8
                code[pos + 1] = alphabet[lngDigit];
                code[pos] = alphabet[latDigit];
            }
            code[8] = '+';
            return new string(code);
        }

        /// <summary>
        /// Công thức Haversine tính khoảng cách mặt cầu chính xác tới từng mét
        /// </summary>
        public static double CalculateHaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000.0; // Bán kính Trái Đất (m)
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double rLat1 = lat1 * Math.PI / 180.0;
            double rLat2 = lat2 * Math.PI / 180.0;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(rLat1) * Math.Cos(rLat2) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        public static string CalculateBearingDirection(double lat1, double lon1, double lat2, double lon2)
        {
            double rLat1 = lat1 * Math.PI / 180.0;
            double rLat2 = lat2 * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;

            double y = Math.Sin(dLon) * Math.Cos(rLat2);
            double x = Math.Cos(rLat1) * Math.Sin(rLat2) -
                       Math.Sin(rLat1) * Math.Cos(rLat2) * Math.Cos(dLon);
            double brng = (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;

            string[] dirs = { "Bắc", "Đông Bắc", "Đông", "Đông Nam", "Nam", "Tây Nam", "Tây", "Tây Bắc" };
            int idx = (int)Math.Round(brng / 45.0) % 8;
            return $"Hướng {dirs[idx]} ({Math.Round(brng)}°)";
        }
    }
}

