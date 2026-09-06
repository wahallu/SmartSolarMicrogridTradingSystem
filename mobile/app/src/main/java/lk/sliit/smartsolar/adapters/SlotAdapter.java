/*
 * =============================================================================
 * File        : SlotAdapter.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : adapters
 * Description : RecyclerView adapter for the energy slot picker on the new and
 *               edit booking screens. It supports a single selection, drawing
 *               the chosen slot with the amber brand highlight, and renders any
 *               slot the API marked as not bookable in a disabled state so the
 *               user cannot pick a window the server would refuse.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.adapters;

import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.core.content.ContextCompat;
import androidx.recyclerview.widget.RecyclerView;

import com.google.android.material.card.MaterialCardView;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.models.SlotModel;

/** Binds a list of energy slots to the booking slot picker, one selection at a time. */
public class SlotAdapter extends RecyclerView.Adapter<SlotAdapter.ViewHolder> {

    /** Callback raised when the user picks a bookable slot. */
    public interface OnSlotClick {

        /** Delivers the newly selected slot to the hosting activity. */
        void onClick(SlotModel slot);
    }

    /** Index of the highlighted row, or -1 when nothing has been chosen yet. */
    private int selectedPosition = -1;

    private List<SlotModel> items;
    private final OnSlotClick listener;

    /** Creates the adapter with no selection callback. */
    public SlotAdapter(List<SlotModel> items) {
        // Used when the screen only reads getSelected() at submit time.
        this(items, null);
    }

    /** Creates the adapter over an initial list and a selection listener. */
    public SlotAdapter(List<SlotModel> items, OnSlotClick listener) {
        // A null list is tolerated so the screen can build the adapter before its first API call.
        this.items = items == null ? new ArrayList<SlotModel>() : items;
        this.listener = listener;
    }

    /** Swaps in a freshly loaded list and clears the current selection. */
    public void updateData(List<SlotModel> newItems) {
        // The selection is dropped because the previous index would point at a different slot after
        // the user changes the node or the date.
        this.items = newItems == null ? new ArrayList<SlotModel>() : newItems;
        this.selectedPosition = -1;
        notifyDataSetChanged();
    }

    /** The slot the user picked, or null while nothing is chosen. */
    public SlotModel getSelected() {
        // The booking screen calls this to build its request body.
        if (selectedPosition < 0 || selectedPosition >= items.size()) {
            return null;
        }
        return items.get(selectedPosition);
    }

    /** Moves the highlight to the given row, or clears it with -1. */
    public void setSelectedPosition(int position) {
        // Only the two affected rows are redrawn, which keeps the picker from flickering.
        int previous = selectedPosition;
        selectedPosition = position;
        if (previous >= 0 && previous < items.size()) {
            notifyItemChanged(previous);
        }
        if (selectedPosition >= 0 && selectedPosition < items.size()) {
            notifyItemChanged(selectedPosition);
        }
    }

    /** Inflates one slot row. */
    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        // attachToRoot is false because the RecyclerView performs the attach itself.
        View row = LayoutInflater.from(parent.getContext())
                .inflate(R.layout.item_slot, parent, false);
        return new ViewHolder(row);
    }

    /** Fills one slot row and applies its selected / disabled appearance. */
    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        // Every figure here was produced by the Web API; the row only presents it.
        final SlotModel slot = items.get(position);

        holder.header.setText(slot.getSlotCode() + "   " + slot.getTimeRange());
        holder.type.setText(slot.getSlotTypeLabel());
        holder.capacity.setText(
                String.format(Locale.getDefault(), "%.0f kWh", slot.getEnergyCapacityKwh()));
        holder.bay.setText(String.format(Locale.getDefault(), "Bay %d", slot.getBatterySlotNumber()));

        // isBookable() is the SERVER's verdict on rule R8 - it already weighed the remaining
        // capacity, any operator block and whether the window has started. The UI only obeys it and
        // never re-derives availability from bookedCount, maxBookings or the clock.
        boolean bookable = slot.isBookable();
        holder.unavailable.setVisibility(bookable ? View.GONE : View.VISIBLE);
        holder.card.setAlpha(bookable ? 1f : 0.5f);
        holder.card.setClickable(bookable);
        holder.card.setFocusable(bookable);

        // Highlight the chosen row with the amber brand colours; every other row stays neutral.
        boolean selected = position == selectedPosition;
        int density = Math.round(holder.itemView.getResources().getDisplayMetrics().density);
        holder.card.setCardBackgroundColor(ContextCompat.getColor(holder.itemView.getContext(),
                selected ? R.color.solar_50 : R.color.white));
        holder.card.setStrokeColor(ContextCompat.getColor(holder.itemView.getContext(),
                selected ? R.color.solar_500 : R.color.slate_200));
        holder.card.setStrokeWidth(selected ? 2 * density : density);

        if (!bookable) {
            // A refused slot must not respond to taps at all, so the listener is detached.
            holder.card.setOnClickListener(null);
            return;
        }

        holder.card.setOnClickListener(new View.OnClickListener() {

            /** Selects the tapped slot and tells the activity about it. */
            @Override
            public void onClick(View view) {
                // getAdapterPosition is re-read because rows are recycled and may have moved.
                int current = holder.getAdapterPosition();
                if (current == RecyclerView.NO_POSITION) {
                    return;
                }
                setSelectedPosition(current);
                if (listener != null) {
                    listener.onClick(items.get(current));
                }
            }
        });
    }

    /** Number of slots currently held. */
    @Override
    public int getItemCount() {
        // Drives the empty state check performed by the hosting activity.
        return items.size();
    }

    /** Holds the views of one slot row so they are looked up only once. */
    static class ViewHolder extends RecyclerView.ViewHolder {

        final MaterialCardView card;
        final TextView header;
        final TextView type;
        final TextView capacity;
        final TextView bay;
        final TextView unavailable;

        /** Caches the row's child views. */
        ViewHolder(View row) {
            // findViewById is used directly because view binding is not permitted on this project.
            super(row);
            card = row.findViewById(R.id.card_slot);
            header = row.findViewById(R.id.text_slot_header);
            type = row.findViewById(R.id.text_slot_type);
            capacity = row.findViewById(R.id.text_capacity);
            bay = row.findViewById(R.id.text_bay);
            unavailable = row.findViewById(R.id.text_unavailable);
        }
    }
}
