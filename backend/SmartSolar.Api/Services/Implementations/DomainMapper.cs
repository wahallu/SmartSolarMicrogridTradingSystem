/* =============================================================================
 * File        : DomainMapper.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Implementations
 * Description : Converts stored domain records into client-facing API models.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-21
 * =============================================================================
 */

using SmartSolar.Api.DTOs.Reservations;
using SmartSolar.Api.DTOs.Slots;
using SmartSolar.Api.DTOs.Stations;
using SmartSolar.Api.DTOs.Users;
using SmartSolar.Api.Models;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.Services.Implementations;

/// <summary>Static projections from persisted entities to wire DTOs.</summary>
public static class DomainMapper
{
    /// <summary>Projects an account, deliberately omitting all password material.</summary>
    public static UserDto ToDto(this User user)
    {
        // Only fields the clients are allowed to render are copied across.
        return new UserDto
        {
            Id = user.Id,
            Nic = user.Nic,
            Username = user.Username,
            Email = user.Email,
            FullName = user.FullName,
            Phone = user.Phone,
            Address = user.Address,
            Role = user.Role.ToString(),
            Status = user.Status.ToString(),
            DeactivationRequested = user.DeactivationRequested,
            DeactivationRequestedAt = user.DeactivationRequestedAt,
            DeactivationReason = user.DeactivationReason,
            AssignedStationId = user.AssignedStationId,
            AssignedStationCode = user.AssignedStationCode,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    /// <summary>Projects a microgrid node, flattening the nested GPS position.</summary>
    public static StationDto ToDto(this SolarStationInfo station, long activeReservationCount = 0)
    {
        // Latitude and longitude are flattened because both map SDKs expect plain numbers.
        return new StationDto
        {
            Id = station.Id,
            StationCode = station.StationCode,
            Name = station.Name,
            Type = station.Type.ToString(),
            Address = station.Address,
            City = station.City,
            Latitude = station.Location.Latitude,
            Longitude = station.Location.Longitude,
            CapacityKwh = station.CapacityKwh,
            TotalBatterySlots = station.TotalBatterySlots,
            AvailableBatterySlots = station.AvailableBatterySlots,
            OperatingHours = new OperatingHoursDto
            {
                OpenTime = station.OperatingHours.OpenTime,
                CloseTime = station.OperatingHours.CloseTime
            },
            WeeklySchedule = station.WeeklySchedule
                .OrderBy(d => d.DayOfWeek)
                .Select(d => new DayScheduleDto
                {
                    DayOfWeek = d.DayOfWeek,
                    DayName = DayName(d.DayOfWeek),
                    IsOpen = d.IsOpen,
                    OpenTime = d.OpenTime,
                    CloseTime = d.CloseTime
                })
                .ToList(),
            ContactNumber = station.ContactNumber,
            Status = station.Status.ToString(),
            ActiveReservationCount = activeReservationCount,
            CreatedAt = station.CreatedAt,
            UpdatedAt = station.UpdatedAt
        };
    }

    /// <summary>Projects a node together with its distance from the caller, for the map screens.</summary>
    public static NearbyStationDto ToNearbyDto(this SolarStationInfo station, double distanceKm, long activeReservationCount = 0)
    {
        // Reuses the station projection then adds the computed great circle distance.
        var dto = station.ToDto(activeReservationCount);
        return new NearbyStationDto
        {
            Id = dto.Id,
            StationCode = dto.StationCode,
            Name = dto.Name,
            Type = dto.Type,
            Address = dto.Address,
            City = dto.City,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            CapacityKwh = dto.CapacityKwh,
            TotalBatterySlots = dto.TotalBatterySlots,
            AvailableBatterySlots = dto.AvailableBatterySlots,
            OperatingHours = dto.OperatingHours,
            WeeklySchedule = dto.WeeklySchedule,
            ContactNumber = dto.ContactNumber,
            Status = dto.Status,
            ActiveReservationCount = dto.ActiveReservationCount,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            DistanceKm = Math.Round(distanceKm, 2)
        };
    }

    /// <summary>Projects a booking slot and states whether it would accept a booking now.</summary>
    public static SlotDto ToDto(this EnergyBookingSlot slot, DateTime utcNow, bool stationActive = true)
    {
        // IsBookable is rule R8 evaluated on the server so clients never re-derive it.
        var remaining = Math.Max(0, slot.MaxBookings - slot.BookedCount);
        return new SlotDto
        {
            Id = slot.Id,
            StationId = slot.StationId,
            StationCode = slot.StationCode,
            StationName = slot.StationName,
            SlotCode = slot.SlotCode,
            SlotDate = slot.SlotDate,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            StartUtc = slot.StartUtc,
            EndUtc = slot.EndUtc,
            SlotType = slot.SlotType.ToString(),
            EnergyCapacityKwh = slot.EnergyCapacityKwh,
            BatterySlotNumber = slot.BatterySlotNumber,
            MaxBookings = slot.MaxBookings,
            BookedCount = slot.BookedCount,
            RemainingCapacity = remaining,
            Status = slot.Status.ToString(),
            IsBookable = stationActive
                         && slot.Status == SlotStatus.Available
                         && remaining > 0
                         && slot.StartUtc > utcNow
        };
    }

    /// <summary>
    /// Projects a reservation and evaluates rules R2 and R3 on the server, so the
    /// clients only have to honour the CanModify / CanCancel booleans.
    /// </summary>
    public static ReservationDto ToDto(this EnergyReservation reservation, DateTime utcNow, int noticeHours)
    {
        // Hours of notice remaining drives both the modify and the cancel verdicts.
        var hoursUntilStart = (reservation.ReservationStartUtc - utcNow).TotalHours;
        var isChangeableState = reservation.Status is ReservationStatus.Pending or ReservationStatus.Approved;
        var hasNotice = hoursUntilStart >= noticeHours;

        return new ReservationDto
        {
            Id = reservation.Id,
            ReservationNo = reservation.ReservationNo,
            ProsumerNic = reservation.ProsumerNic,
            ProsumerName = reservation.ProsumerName,
            ProsumerPhone = reservation.ProsumerPhone,
            StationId = reservation.StationId,
            StationCode = reservation.StationCode,
            StationName = reservation.StationName,
            SlotId = reservation.SlotId,
            SlotCode = reservation.SlotCode,
            ReservationStartUtc = reservation.ReservationStartUtc,
            ReservationEndUtc = reservation.ReservationEndUtc,
            StartTime = reservation.StartTime,
            EndTime = reservation.EndTime,
            SlotType = reservation.SlotType.ToString(),
            EnergyKwh = reservation.EnergyKwh,
            DeliveredKwh = reservation.DeliveredKwh,
            Status = reservation.Status.ToString(),
            QrToken = reservation.QrToken,
            QrIssuedAt = reservation.QrIssuedAt,
            ApprovedBy = reservation.ApprovedBy,
            ApprovedAt = reservation.ApprovedAt,
            CompletedBy = reservation.CompletedBy,
            CompletedAt = reservation.CompletedAt,
            CancelledBy = reservation.CancelledBy,
            CancelledAt = reservation.CancelledAt,
            CancelReason = reservation.CancelReason,
            RejectedReason = reservation.RejectedReason,
            OperatorNotes = reservation.OperatorNotes,
            CanModify = isChangeableState && hasNotice,
            CanCancel = isChangeableState && hasNotice,
            HoursUntilStart = Math.Round(hoursUntilStart, 2),
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt
        };
    }

    /// <summary>Turns a 0..6 index into an English day name for display.</summary>
    public static string DayName(int dayOfWeek)
    {
        // Guard keeps a corrupt document from throwing while rendering a list.
        return dayOfWeek switch
        {
            0 => "Sunday",
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            _ => "Unknown"
        };
    }

    /// <summary>Builds the default open-all-week schedule used when a node omits one.</summary>
    public static List<DaySchedule> DefaultWeeklySchedule(string openTime = "06:00", string closeTime = "22:00")
    {
        // Seven rows are always produced so the clients can render a fixed size table.
        return Enumerable.Range(0, 7)
            .Select(d => new DaySchedule
            {
                DayOfWeek = d,
                IsOpen = true,
                OpenTime = openTime,
                CloseTime = closeTime
            })
            .ToList();
    }
}
