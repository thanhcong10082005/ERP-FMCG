// ============================================================
// LoadingOverlay
// ============================================================

import React from 'react';

interface Props {
  message?: string;
}

export const LoadingOverlay: React.FC<Props> = ({
  message = 'Đang xử lý...',
}) => (
  <div className="loading-overlay">
    <div className="bg-white rounded-2xl p-6 text-center shadow-2xl max-w-xs w-full mx-4">
      <div className="w-12 h-12 border-4 border-primary border-t-transparent rounded-full animate-spin mx-auto mb-4" />
      <p className="font-medium text-text-primary">{message}</p>
    </div>
  </div>
);
