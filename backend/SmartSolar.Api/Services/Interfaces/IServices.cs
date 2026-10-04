/* =============================================================================
 * File        : IServices.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Interfaces
 * Description : Defines the service contracts used by the API controllers.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-20
 * =============================================================================
 */

using SmartSolar.Api.Common;
using SmartSolar.Api.DTOs.Auth;
using SmartSolar.Api.DTOs.Reservations;
using SmartSolar.Api.DTOs.Slots;
using SmartSolar.Api.DTOs.Stations;
using SmartSolar.Api.DTOs.Users;
using SmartSolar.Api.Security;

namespace SmartSolar.Api.Services.Interfaces;

/// <summary>Authentication, self registration and password maintenance.</summary>
public interface IAuthService
{
    /// <summary>Validates credentials and issues a JWT (rules R5 and R6 apply).</summary>
    Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Registers a solar prosumer with NIC as the primary key (rule R7).</summary>
    Task<ServiceResult<UserDto>> RegisterProsumerAsync(RegisterProsumerRequest request, CancellationToken ct = default);

    /// <summary>Returns the profile of the caller identified by the bearer token.</summary>
    Task<ServiceResult<UserDto>> GetCurrentUserAsync(CurrentUser caller, CancellationToken ct = default);

    /// <summary>Changes the caller's own password after verifying the current one.</summary>
    Task<ServiceResult<bool>> ChangePasswordAsync(CurrentUser caller, ChangePasswordRequest request, CancellationToken ct = default);
}

/// <summary>Administration of Backoffice and Grid Operator accounts (rule R10).</summary>
public interface IUserService
{
    /// <summary>Paged, filtered list of web user accounts.</summary>
    Task<ServiceResult<PagedResult<UserDto>>> GetUsersAsync(UserQuery query, CancellationToken ct = default);

    /// <summary>Single account by its Mongo identifier.</summary>
    Task<ServiceResult<UserDto>> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Creates a Backoffice or Grid Operator account.</summary>
    Task<ServiceResult<UserDto>> CreateUserAsync(CreateUserRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Edits an existing web user account.</summary>
    Task<ServiceResult<UserDto>> UpdateUserAsync(string id, UpdateUserRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Returns an account to Active - Backoffice only (rule R5).</summary>
    Task<ServiceResult<UserDto>> ActivateAsync(string id, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Blocks an account from logging in.</summary>
    Task<ServiceResult<UserDto>> DeactivateAsync(string id, DeactivateRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Sets a new password on behalf of a user.</summary>
    Task<ServiceResult<bool>> ResetPasswordAsync(string id, ResetPasswordRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Permanently removes an account; refuses to delete the caller.</summary>
    Task<ServiceResult<bool>> DeleteAsync(string id, CurrentUser caller, CancellationToken ct = default);
}

/// <summary>Solar prosumer management, keyed by National Identity Card number.</summary>
public interface IProsumerService
{
    /// <summary>Paged, filtered list of prosumers.</summary>
    Task<ServiceResult<PagedResult<UserDto>>> GetProsumersAsync(UserQuery query, CancellationToken ct = default);

    /// <summary>Prosumers whose accounts are still awaiting back office activation.</summary>
    Task<ServiceResult<List<UserDto>>> GetPendingActivationsAsync(CancellationToken ct = default);

    /// <summary>Single prosumer by NIC; a prosumer caller may only read their own record.</summary>
    Task<ServiceResult<UserDto>> GetByNicAsync(string nic, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Creates a prosumer from the back office (rule R7).</summary>
    Task<ServiceResult<UserDto>> CreateAsync(CreateProsumerRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Edits a prosumer profile - self service or back office (rule R11).</summary>
    Task<ServiceResult<UserDto>> UpdateAsync(string nic, UpdateProsumerRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Reactivates a prosumer - Backoffice only (rule R5).</summary>
    Task<ServiceResult<UserDto>> ActivateAsync(string nic, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Deactivates a prosumer account.</summary>
    Task<ServiceResult<UserDto>> DeactivateAsync(string nic, DeactivateRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Raises the prosumer's own deactivation request from the mobile app.</summary>
    Task<ServiceResult<UserDto>> RequestDeactivationAsync(string nic, DeactivateRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Removes a prosumer record entirely - Backoffice only.</summary>
    Task<ServiceResult<bool>> DeleteAsync(string nic, CurrentUser caller, CancellationToken ct = default);
}

/// <summary>Microgrid node management, including the map projection.</summary>
public interface IStationService
{
    /// <summary>Paged, filtered list of microgrid nodes.</summary>
    Task<ServiceResult<PagedResult<StationDto>>> GetStationsAsync(StationQuery query, CancellationToken ct = default);

    /// <summary>Single node by identifier.</summary>
    Task<ServiceResult<StationDto>> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Active nodes within a radius of a point, ordered by distance.</summary>
    Task<ServiceResult<List<NearbyStationDto>>> GetNearbyAsync(NearbyStationQuery query, CancellationToken ct = default);

    /// <summary>Registers a new node with its GPS position and capacity.</summary>
    Task<ServiceResult<StationDto>> CreateAsync(CreateStationRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Edits a node.</summary>
    Task<ServiceResult<StationDto>> UpdateAsync(string id, UpdateStationRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Replaces the seven day operating schedule.</summary>
    Task<ServiceResult<StationDto>> UpdateScheduleAsync(string id, UpdateScheduleRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Updates the free battery bay count maintained by the operator.</summary>
    Task<ServiceResult<StationDto>> UpdateAvailabilityAsync(string id, UpdateAvailabilityRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Returns a node to service.</summary>
    Task<ServiceResult<StationDto>> ActivateAsync(string id, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Takes a node out of service - refused while future bookings exist (rule R4).</summary>
    Task<ServiceResult<StationDto>> DeactivateAsync(string id, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Deletes a node - refused when any reservation references it.</summary>
    Task<ServiceResult<bool>> DeleteAsync(string id, CurrentUser caller, CancellationToken ct = default);
}

/// <summary>Energy booking slot management.</summary>
public interface ISlotService
{
    /// <summary>Paged, filtered list of slots.</summary>
    Task<ServiceResult<PagedResult<SlotDto>>> GetSlotsAsync(SlotQuery query, CancellationToken ct = default);

    /// <summary>Slots that would currently accept a booking (rule R8).</summary>
    Task<ServiceResult<List<SlotDto>>> GetAvailableAsync(string stationId, DateTime? date, CancellationToken ct = default);

    /// <summary>Single slot by identifier.</summary>
    Task<ServiceResult<SlotDto>> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Publishes one new slot.</summary>
    Task<ServiceResult<SlotDto>> CreateAsync(CreateSlotRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Publishes a whole date range of slots in one call.</summary>
    Task<ServiceResult<GenerateSlotsResultDto>> GenerateAsync(GenerateSlotsRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Edits a slot; refuses changes that would strand existing bookings.</summary>
    Task<ServiceResult<SlotDto>> UpdateAsync(string id, UpdateSlotRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Blocks a slot from further bookings.</summary>
    Task<ServiceResult<SlotDto>> BlockAsync(string id, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Deletes a slot - refused while it carries bookings.</summary>
    Task<ServiceResult<bool>> DeleteAsync(string id, CurrentUser caller, CancellationToken ct = default);
}

/// <summary>The reservation workflow and every rule that governs it.</summary>
public interface IReservationService
{
    /// <summary>Paged, filtered list for the staff search screens.</summary>
    Task<ServiceResult<PagedResult<ReservationDto>>> GetReservationsAsync(ReservationQuery query, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Single reservation; owner or staff only (rule R11).</summary>
    Task<ServiceResult<ReservationDto>> GetByIdAsync(string id, CurrentUser caller, CancellationToken ct = default);

    /// <summary>A prosumer's own bookings, sliced by scope.</summary>
    Task<ServiceResult<List<ReservationDto>>> GetMyReservationsAsync(ReservationScope scope, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Full booking history for a NIC.</summary>
    Task<ServiceResult<List<ReservationDto>>> GetHistoryAsync(string? nic, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Live dashboard counters, scoped to a station and/or a prosumer.</summary>
    Task<ServiceResult<DashboardDto>> GetDashboardAsync(string? stationId, string? nic, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Creates a booking - enforces rules R1, R8, R12 and R13.</summary>
    Task<ServiceResult<ReservationDto>> CreateAsync(CreateReservationRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Changes a booking - enforces the 12 hour notice rule R2.</summary>
    Task<ServiceResult<ReservationDto>> UpdateAsync(string id, UpdateReservationRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Cancels a booking - enforces the 12 hour notice rule R3.</summary>
    Task<ServiceResult<ReservationDto>> CancelAsync(string id, CancelReservationRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Approves a booking and issues its QR token.</summary>
    Task<ServiceResult<ReservationDto>> ApproveAsync(string id, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Rejects a booking with a reason.</summary>
    Task<ServiceResult<ReservationDto>> RejectAsync(string id, RejectReservationRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Returns the QR payload and a rendered PNG for an approved booking.</summary>
    Task<ServiceResult<QrCodeDto>> GetQrCodeAsync(string id, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Validates a scanned token against the server (rule R9).</summary>
    Task<ServiceResult<ReservationDto>> VerifyQrAsync(VerifyQrRequest request, CurrentUser caller, CancellationToken ct = default);

    /// <summary>Finalises the energy transfer after a successful scan.</summary>
    Task<ServiceResult<ReservationDto>> CompleteAsync(CompleteReservationRequest request, CurrentUser caller, CancellationToken ct = default);
}

/// <summary>Renders QR images for approved reservations.</summary>
public interface IQrCodeGenerator
{
    /// <summary>Encodes the payload as a PNG and returns it base64 encoded.</summary>
    string GeneratePngBase64(string payload, int pixelsPerModule = 10);

    /// <summary>Creates a cryptographically random, URL safe reservation token.</summary>
    string CreateToken();
}
