/* =============================================================================
 * File        : src/App.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Application Shell
 * Description : Defines the public pages and role-protected application
 *               routes.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-24
 * =============================================================================
 */

import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './components/ui';
import { Layout } from './components/Layout';
import { ProtectedRoute } from './components/ProtectedRoute';

import HomePage from './pages/HomePage';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import ReservationsPage from './pages/ReservationsPage';
import ReservationDetailPage from './pages/ReservationDetailPage';
import StationsPage from './pages/StationsPage';
import SlotsPage from './pages/SlotsPage';
import ProsumersPage from './pages/ProsumersPage';
import PendingActivationsPage from './pages/PendingActivationsPage';
import UsersPage from './pages/UsersPage';
import ProfilePage from './pages/ProfilePage';
import MapPage from './pages/MapPage';

/** Composes the providers and the route table. */
export default function App() {
  // AuthProvider must sit above the routes because ProtectedRoute reads the session.
  return (
    <BrowserRouter>
      <AuthProvider>
        <ToastProvider>
          <Routes>
            {/* ---- Public ---- */}
            <Route path="/" element={<HomePage />} />
            <Route path="/login" element={<LoginPage />} />

            {/* ---- Authenticated shell ---- */}
            <Route
              element={
                <ProtectedRoute>
                  <Layout />
                </ProtectedRoute>
              }
            >
              <Route path="/dashboard" element={<DashboardPage />} />

              {/* Every signed in role may work with reservations; the API scopes a
                  prosumer to their own rows (rule R11). */}
              <Route path="/reservations" element={<ReservationsPage />} />
              <Route path="/reservations/:id" element={<ReservationDetailPage />} />

              <Route path="/stations" element={<StationsPage />} />
              <Route path="/map" element={<MapPage />} />
              <Route path="/profile" element={<ProfilePage />} />

              {/* Booking slots are maintained by staff only. */}
              <Route
                path="/slots"
                element={
                  <ProtectedRoute roles={['Backoffice', 'GridOperator']}>
                    <SlotsPage />
                  </ProtectedRoute>
                }
              />

              <Route
                path="/prosumers"
                element={
                  <ProtectedRoute roles={['Backoffice', 'GridOperator']}>
                    <ProsumersPage />
                  </ProtectedRoute>
                }
              />

              {/* The pending activation queue is a Backoffice responsibility (rule R5). */}
              <Route
                path="/prosumers/pending"
                element={
                  <ProtectedRoute roles={['Backoffice']}>
                    <PendingActivationsPage />
                  </ProtectedRoute>
                }
              />

              {/* Web user administration is Backoffice only (rule R10). */}
              <Route
                path="/users"
                element={
                  <ProtectedRoute roles={['Backoffice']}>
                    <UsersPage />
                  </ProtectedRoute>
                }
              />
            </Route>

            {/* Anything unrecognised returns to the landing page. */}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </ToastProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}
