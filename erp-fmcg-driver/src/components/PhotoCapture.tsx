// ============================================================
// PhotoCapture — Camera capture cho hàng rớt / bằng chứng
// ============================================================

import React, { useRef, useState } from 'react';

interface Props {
  onCapture: (dataUrl: string) => void;
  label?: string;
  required?: boolean;
}

export const PhotoCapture: React.FC<Props> = ({
  onCapture,
  label = 'Chụp ảnh',
  required = false,
}) => {
  const inputRef = useRef<HTMLInputElement>(null);
  const [preview, setPreview] = useState<string | null>(null);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = () => {
      const dataUrl = reader.result as string;
      setPreview(dataUrl);
      onCapture(dataUrl);
    };
    reader.readAsDataURL(file);
  };

  return (
    <div className="flex flex-col gap-2">
      <label className="input-label">
        {label}
        {required && <span className="text-danger ml-1">*</span>}
      </label>

      {preview ? (
        <div className="relative rounded-xl overflow-hidden border-2 border-success">
          <img
            src={preview}
            alt="Preview"
            className="w-full h-48 object-cover"
          />
          <div className="absolute top-2 right-2 flex gap-1">
            <button
              type="button"
              className="btn btn-primary text-xs py-1 px-3"
              onClick={() => inputRef.current?.click()}
            >
              Chụp lại
            </button>
          </div>
          <div className="absolute bottom-2 left-2 bg-success text-white text-xs px-2 py-1 rounded-full flex items-center gap-1">
            <CheckIcon /> Đã chụp
          </div>
        </div>
      ) : (
        <button
          type="button"
          className="w-full h-48 border-2 border-dashed border-border rounded-xl flex flex-col items-center justify-center gap-2 text-text-secondary hover:border-primary hover:text-primary transition-colors"
          onClick={() => inputRef.current?.click()}
        >
          <CameraIcon />
          <span className="text-sm font-medium">{label}</span>
          <span className="text-xs">Tap to capture</span>
        </button>
      )}

      <input
        ref={inputRef}
        type="file"
        accept="image/*"
        capture="environment"
        className="hidden"
        onChange={handleFileChange}
      />
    </div>
  );
};

const CameraIcon = () => (
  <svg className="w-12 h-12" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
      d="M3 9a2 2 0 012-2h.93a2 2 0 001.664-.89l.812-1.22A2 2 0 0110.07 4h3.86a2 2 0 011.664.89l.812 1.22A2 2 0 0018.07 7H19a2 2 0 012 2v9a2 2 0 01-2 2H5a2 2 0 01-2-2V9z" />
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
      d="M15 13a3 3 0 11-6 0 3 3 0 016 0z" />
  </svg>
);

const CheckIcon = () => (
  <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3}
      d="M5 13l4 4L19 7" />
  </svg>
);
