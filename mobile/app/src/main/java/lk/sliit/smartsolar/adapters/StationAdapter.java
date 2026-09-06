/*
 * =============================================================================
 * File        : StationAdapter.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : adapters
 * Description : RecyclerView adapter for the microgrid node lists - the nearby
 *               node search and the station picker used when creating a
 *               booking. Each row shows the node code, a status pill, the
 *               address, the rated capacity, battery bay availability and, when
 *               the API supplied one, the distance from the user.
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
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.core.content.ContextCompat;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.models.StationModel;

/** Binds a list of microgrid nodes to the station RecyclerViews. */
public class StationAdapter extends RecyclerView.Adapter<StationAdapter.ViewHolder> {

    /** Callback raised when the user taps a node card. */
    public interface OnStationClick {

        /** Delivers the tapped node to the hosting activity. */
        void onClick(StationModel station);
    }

    private List<StationModel> items;
    private final OnStationClick listener;

    /** Creates the adapter over an initial list and a tap listener. */
    public StationAdapter(List<StationModel> items, OnStationClick listener) {
        // A null list is tolerated so a screen can build the adapter before its first API call.
        this.items = items == null ? new ArrayList<StationModel>() : items;
        this.listener = listener;
    }

    /** Swaps in a freshly loaded list and redraws every row. */
    public void updateData(List<StationModel> newItems) {
        // Node lists are short, so a full redraw is simpler and fast enough here.
        this.items = newItems == null ? new ArrayList<StationModel>() : newItems;
        notifyDataSetChanged();
    }

    /** Inflates one node card. */
    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        // attachToRoot is false because the RecyclerView performs the attach itself.
        View row = LayoutInflater.from(parent.getContext())
                .inflate(R.layout.item_station, parent, false);
        return new ViewHolder(row);
    }

    /** Fills one node card with the values of a single station. */
    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        // Every figure shown here arrived from the Web API already computed.
        final StationModel station = items.get(position);

        holder.stationCode.setText(station.getStationCode());
        holder.name.setText(station.getName());
        holder.capacity.setText(String.format(Locale.getDefault(), "%.0f kWh", station.getCapacityKwh()));
        holder.slots.setText(station.getSlotsLabel());

        // Address and city read as one line, skipping the comma when the city is missing.
        String city = station.getCity();
        String address = station.getAddress();
        holder.address.setText(city == null || city.isEmpty() ? address : address + ", " + city);

        // The distance was measured by the SERVER in GET /stations/nearby using the coordinates the
        // phone sent. The app never runs a haversine of its own - the field is display only, and it
        // is simply absent (getDistanceLabel returns "") on endpoints that do not rank by distance.
        String distance = station.getDistanceLabel();
        if (distance.isEmpty()) {
            holder.distance.setVisibility(View.GONE);
        } else {
            holder.distance.setVisibility(View.VISIBLE);
            holder.distance.setText(distance);
        }

        // The status name comes straight from the API's station status field.
        holder.status.setText(station.getStatus());
        boolean active = station.isActive();
        holder.status.setTextColor(ContextCompat.getColor(holder.itemView.getContext(),
                active ? R.color.status_completed_text : R.color.status_cancelled_text));

        // The single bg_status_chip pill drawable is recoloured at runtime rather than duplicated
        // per status, exactly as the booking rows do.
        Drawable pill = holder.status.getBackground();
        if (pill instanceof GradientDrawable) {
            // mutate() keeps this chip's fill from leaking into recycled sibling rows.
            GradientDrawable shape = (GradientDrawable) pill.mutate();
            shape.setColor(ContextCompat.getColor(holder.itemView.getContext(),
                    active ? R.color.status_completed_bg : R.color.status_cancelled_bg));
        }

        // An out of service node is dimmed rather than hidden so the user understands why it cannot
        // be booked. Whether it is bookable at all remains a server decision at submit time.
        holder.itemView.setAlpha(active ? 1f : 0.55f);

        holder.itemView.setOnClickListener(new View.OnClickListener() {

            /** Hands the tapped node back to the activity. */
            @Override
            public void onClick(View view) {
                // The activity owns navigation; the adapter only reports the tap.
                if (listener != null) {
                    listener.onClick(station);
                }
            }
        });
    }

    /** Number of nodes currently held. */
    @Override
    public int getItemCount() {
        // Drives the empty state check performed by the hosting activity.
        return items.size();
    }

    /** Holds the views of one node card so they are looked up only once. */
    static class ViewHolder extends RecyclerView.ViewHolder {

        final TextView stationCode;
        final TextView status;
        final TextView name;
        final TextView address;
        final TextView capacity;
        final TextView slots;
        final TextView distance;

        /** Caches the row's child views. */
        ViewHolder(View row) {
            // findViewById is used directly because view binding is not permitted on this project.
            super(row);
            stationCode = row.findViewById(R.id.text_station_code);
            status = row.findViewById(R.id.text_status);
            name = row.findViewById(R.id.text_station_name);
            address = row.findViewById(R.id.text_address);
            capacity = row.findViewById(R.id.text_capacity);
            slots = row.findViewById(R.id.text_slots);
            distance = row.findViewById(R.id.text_distance);
        }
    }
}
