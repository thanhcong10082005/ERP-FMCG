// ============================================================
// LoginScreen — Step 1a: Tài xế đăng nhập
// ============================================================

import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { devLogin, syncMasterData } from '../services/authService';
import { useDriverStore } from '../store/driverStore';

export const LoginScreen: React.FC = () => {
  const [identifier, setIdentifier] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();
  const { setUser } = useDriverStore();

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!identifier.trim()) {
      setError('Vui lòng nhập SĐT hoặc mã định danh');
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const result = await devLogin(identifier.trim());
      setUser(result.user, result.access_token);

      // Sync master data
      await syncMasterData(result.user.tenant_id);

      // Navigate to home
      navigate('/home', { replace: true });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Đăng nhập thất bại';
      setError(msg);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="screen flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        {/* Logo / Branding */}
        <div className="text-center mb-8">
          <div className="w-20 h-20 bg-primary rounded-2xl flex items-center justify-center mx-auto mb-4 shadow-lg">
            <TruckIcon />
          </div>
          <h1 className="text-2xl font-extrabold text-text-primary">Driver App</h1>
          <p className="text-text-secondary mt-1">ERP-FMCG — Ứng dụng giao hàng</p>
        </div>

        {/* Form */}
        <form onSubmit={handleLogin} className="space-y-4">
          <div className="input-group">
            <label className="input-label">Số điện thoại / Mã định danh</label>
            <input
              type="tel"
              inputMode="tel"
              className="input text-lg py-4"
              placeholder="Nhập SĐT hoặc mã nhân viên..."
              value={identifier}
              onChange={(e) => setIdentifier(e.target.value)}
              autoComplete="tel"
              autoFocus
            />
          </div>

          {error && (
            <div className="bg-danger-50 border border-danger-200 rounded-xl p-3">
              <p className="text-sm text-danger">{error}</p>
            </div>
          )}

          <button
            type="submit"
            className="btn btn-primary w-full text-lg py-4 rounded-2xl"
            disabled={loading}
          >
            {loading ? (
              <span className="flex items-center justify-center gap-2">
                <span className="w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin" />
                Đang đăng nhập...
              </span>
            ) : (
              'Đăng nhập'
            )}
          </button>
        </form>

        {/* Dev hints */}
        <div className="mt-6 p-3 bg-slate-100 rounded-xl">
          <p className="text-xs text-text-secondary font-medium mb-1">Gợi ý (Dev):</p>
          <p className="text-xs text-text-secondary">Nhập <code className="bg-white px-1 rounded">1</code> để đăng nhập với user_id=1 (Driver)</p>
          <p className="text-xs text-text-secondary">Nhập <code className="bg-white px-1 rounded">0909123456</code> để đăng nhập theo SĐT</p>
        </div>
      </div>
    </div>
  );
};

const TruckIcon = () => (
  <svg className="w-10 h-10 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
      d="M8.25 18.75a1.5 1.5 0 01-3 0m3 0a1.5 1.5 0 00-3 0m3 0h6m-9 0H3.375a1.125 1.125 0 01-1.125-1.125V14.25m17.25 4.5a1.5 1.5 0 01-3 0m3 0a1.5 1.5 0 00-3 0m3 0h1.125c.621 0 1.129-.504 1.09-1.124a17.902 17.902 0 00-3.213-9.193 2.056 2.056 0 00-1.58-.86H14.25M16.5 18.75h-2.25m0-11.177v-.958c0-.568-.422-1.048-.987-1.106a48.554 48.554 0 00-10.026 0 1.106 1.106 0 00-.987 1.106v7.635m12-6.677v6.677m0 4.5v-4.5m0 0h-12" />
  </svg>
);
