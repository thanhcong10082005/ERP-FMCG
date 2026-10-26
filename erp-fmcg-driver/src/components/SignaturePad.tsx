// ============================================================
// SignaturePad — E-signature canvas
// ============================================================

import React, { useRef, useState, useCallback } from 'react';

interface Props {
  onSave: (dataUrl: string) => void;
  onClear?: () => void;
}

export const SignaturePad: React.FC<Props> = ({ onSave, onClear }) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [isDrawing, setIsDrawing] = useState(false);
  const [hasSignature, setHasSignature] = useState(false);

  const getContext = () => canvasRef.current?.getContext('2d');

  const startDraw = useCallback((e: React.TouchEvent | React.MouseEvent) => {
    const ctx = getContext();
    if (!ctx || !canvasRef.current) return;
    setIsDrawing(true);
    setHasSignature(true);

    const rect = canvasRef.current.getBoundingClientRect();
    const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX;
    const clientY = 'touches' in e ? e.touches[0].clientY : e.clientY;
    ctx.beginPath();
    ctx.moveTo(clientX - rect.left, clientY - rect.top);
  }, []);

  const draw = useCallback((e: React.TouchEvent | React.MouseEvent) => {
    if (!isDrawing) return;
    const ctx = getContext();
    if (!ctx || !canvasRef.current) return;

    const rect = canvasRef.current.getBoundingClientRect();
    const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX;
    const clientY = 'touches' in e ? e.touches[0].clientY : e.clientY;

    ctx.strokeStyle = '#1E40AF';
    ctx.lineWidth = 2.5;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.lineTo(clientX - rect.left, clientY - rect.top);
    ctx.stroke();
  }, [isDrawing]);

  const endDraw = useCallback(() => {
    if (isDrawing) {
      setIsDrawing(false);
    }
  }, [isDrawing]);

  const handleSave = () => {
    if (!canvasRef.current) return;
    const dataUrl = canvasRef.current.toDataURL('image/png');
    onSave(dataUrl);
  };

  const handleClear = () => {
    const ctx = getContext();
    if (!ctx || !canvasRef.current) return;
    ctx.clearRect(0, 0, canvasRef.current.width, canvasRef.current.height);
    setHasSignature(false);
    onClear?.();
  };

  return (
    <div className="flex flex-col gap-2">
      <label className="input-label">
        Chữ ký khách hàng <span className="text-danger">*</span>
      </label>
      <div className="relative border-2 border-dashed border-border rounded-xl overflow-hidden bg-white">
        <canvas
          ref={canvasRef}
          width={340}
          height={150}
          className="w-full touch-none"
          onMouseDown={startDraw}
          onMouseMove={draw}
          onMouseUp={endDraw}
          onMouseLeave={endDraw}
          onTouchStart={startDraw}
          onTouchMove={draw}
          onTouchEnd={endDraw}
        />
        {!hasSignature && (
          <p className="absolute inset-0 flex items-center justify-center text-text-secondary text-sm pointer-events-none">
            Ký tại đây
          </p>
        )}
      </div>
      <div className="flex gap-2">
        <button
          type="button"
          className="btn btn-ghost flex-1 text-sm"
          onClick={handleClear}
          disabled={!hasSignature}
        >
          Xóa chữ ký
        </button>
        <button
          type="button"
          className="btn btn-primary flex-1 text-sm"
          onClick={handleSave}
          disabled={!hasSignature}
        >
          Lưu chữ ký
        </button>
      </div>
    </div>
  );
};
