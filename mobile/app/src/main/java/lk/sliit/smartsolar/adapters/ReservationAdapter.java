/*
 * =============================================================================
 * File        : ReservationAdapter.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : adapters
 * Description : RecyclerView adapter that renders a booking as a card: its
 *               reference, a colour coded status pill, the node, the slot day
 *               and time, the traded energy and how long remains before the
 *               window opens. It serves the "My bookings" and "Node bookings"
 *               list requirements. No business rule is evaluated here - the row
 *               only mirrors the flags the Web API already decided.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.adapters;

import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ImageView;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.core.content.ContextCompat;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.List;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.DateUtils;

/** Binds a list of reservations to the booking list RecyclerViews. */
public class ReservationAdapter extends RecyclerView.Adapter<ReservationAdapter.ViewHolder> {

    /** Callback raised when the user taps a booking card. */
    public interface OnReservationClick {

        /** Delivers the tapped booking to the hosting activity. */
        void onClick(ReservationModel reservation);
    }

    private List<ReservationModel> items;
    private final OnReservationClick listener;

    /** Creates the adapter over an initial list and a tap listener. */
    public ReservationAdapter(List<ReservationModel> items, OnReservationClick listener) {
        // A null list is tolerated so a screen can build the adapter before its first API call.
        this.items = items == null ? new ArrayList<ReservationModel>() : items;
        this.listener = listener;
    }

    /** Swaps in a freshly loaded list and redraws every row. */
    public void updateData(List<ReservationModel> newItems) {
        // The lists are short (one screen of bookings) so a full redraw is cheaper than diffing.
        this.items = newItems == null ? new ArrayList<ReservationModel>() : newItems;
        notifyDataSetChanged();
    }

    /** Inflates one booking card. */
    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        // attachToRoot is false because the RecyclerView performs the attach itself.
        View row = LayoutInflater.from(parent.getContext())
                .inflate(R.layout.item_reservation, parent, false);
        return new ViewHolder(row);
    }

    /** Fills one booking card with the values of a single reservation. */
    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        // Every value shown here was computed by the Web API; the row is a pure display layer.
        final ReservationModel reservation = items.get(position);

        holder.reservationNo.setText(reservation.getReservationNo());
        holder.stationName.setText(reservation.getStationName());
        holder.energy.setText(reservation.getEnergyLabel());

        // Slot day and slot window, both already formatted server side.
        String when = DateUtils.formatDate(reservation.getReservationStartUtc())
                + ", " + reservation.getTimeRange();
        holder.dateTime.setText(when);

        // Cosmetic countdown only. The Edit / Cancel buttons on the detail screen are driven by
        // reservation.canModify() and reservation.canCancel(), which are the SERVER's verdicts on
        // the 12 hour notice rule - this adapter never recomputes them from hoursUntilStart.
        holder.hoursUntil.setText(DateUtils.describeHoursUntil(reservation.getHoursUntilStart()));

        // The status name comes straight from the API's ReservationStatus enum.
        holder.status.setText(reservation.getStatus());
        holder.status.setTextColor(
                ContextCompat.getColor(holder.itemView.getContext(), textColourFor(reservation.getStatus())));

        // One rounded pill drawable (bg_status_chip) is reused for all five statuses: its solid
        // fill is replaced at runtime instead of shipping five near identical drawable files.
        Drawable pill = holder.status.getBackground();
        if (pill instanceof GradientDrawable) {
            // mutate() gives this chip its own state so sibling rows are not recoloured with it.
            GradientDrawable shape = (GradientDrawable) pill.mutate();
            shape.setColor(ContextCompat.getColor(
                    holder.itemView.getContext(), fillColourFor(reservation.getStatus())));
        }

        // hasQrCode() is true only for an approved booking that still holds a server issued token.
        holder.qrBadge.setVisibility(reservation.hasQrCode() ? View.VISIBLE : View.GONE);

        holder.itemView.setOnClickListener(new View.OnClickListener() {

            /** Hands the tapped booking back to the activity. */
            @Override
            public void onClick(View view) {
                // The activity decides where to navigate; the adapter stays free of navigation code.
                if (listener != null) {
                    listener.onClick(reservation);
                }
            }
        });
    }

    /** Number of bookings currently held. */
    @Override
    public int getItemCount() {
        // Drives the empty state check performed by the hosting activity.
        return items.size();
    }

    /** Maps a reservation status onto its pill fill colour. */
    private static int fillColourFor(String status) {
        // Mirrors the badge palette used by the web client so both look like one product.
        if (Constants.RES_APPROVED.equals(status)) {
            return R.color.status_approved_bg;
        }
        if (Constants.RES_COMPLETED.equals(status)) {
            return R.color.status_completed_bg;
        }
        if (Constants.RES_CANCELLED.equals(status)) {
            return R.color.status_cancelled_bg;
        }
        if (Constants.RES_REJECTED.equals(status)) {
            return R.color.status_rejected_bg;
        }
        return R.color.status_pending_bg;
    }

    /** Maps a reservation status onto its pill text colour. */
    private static int textColourFor(String status) {
        // Each fill has a matching dark tone that keeps the label readable.
        if (Constants.RES_APPROVED.equals(status)) {
            return R.color.status_approved_text;
        }
        if (Constants.RES_COMPLETED.equals(status)) {
            return R.color.status_completed_text;
        }
        if (Constants.RES_CANCELLED.equals(status)) {
            return R.color.status_cancelled_text;
        }
        if (Constants.RES_REJECTED.equals(status)) {
            return R.color.status_rejected_text;
        }
        return R.color.status_pending_text;
    }

    /** Holds the views of one booking card so they are looked up only once. */
    static class ViewHolder extends RecyclerView.ViewHolder {

        final TextView reservationNo;
        final TextView status;
        final TextView stationName;
        final TextView dateTime;
        final TextView energy;
        final TextView hoursUntil;
        final ImageView qrBadge;

        /** Caches the row's child views. */
        ViewHolder(View row) {
            // findViewById is used directly because view binding is not permitted on this project.
            super(row);
            reservationNo = row.findViewById(R.id.text_reservation_no);
            status = row.findViewById(R.id.text_status);
            stationName = row.findViewById(R.id.text_station_name);
            dateTime = row.findViewById(R.id.text_date_time);
            energy = row.findViewById(R.id.text_energy);
            hoursUntil = row.findViewById(R.id.text_hours_until);
            qrBadge = row.findViewById(R.id.image_qr_badge);
        }
    }
}
