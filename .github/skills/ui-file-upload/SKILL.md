---
name: ui-file-upload
description: >-
  FileUpload cu drag & drop pentru ERP — upload la MinIO via API,
  preview fișiere, progress bar, validare tip și dimensiune,
  integrat cu React Hook Form.
---

# FileUpload Component

## Când se aplică
Câmpuri de atașare documente: PDF-uri, imagini, contracte, documente scanate.

```tsx
// components/common/FileUpload/FileUpload.tsx
import { useState, useCallback } from 'react';
import { useDropzone } from 'react-dropzone';
import { UploadCloudIcon, XIcon, FileIcon, CheckCircleIcon } from 'lucide-react';
import { Progress } from '@/components/ui/progress';
import { Button } from '@/components/ui/button';
import { api } from '@/lib/axios';
import { cn } from '@/lib/utils';

// Instalare: npm install react-dropzone

export interface UploadedFile {
  id:       string;     // ID returnat de backend (MinIO key)
  name:     string;
  size:     number;
  url?:     string;     // URL preview
}

interface FileUploadProps {
  value?:       UploadedFile[];
  onChange:     (files: UploadedFile[]) => void;
  accept?:      Record<string, string[]>;   // ex: { 'application/pdf': ['.pdf'] }
  maxSize?:     number;                     // bytes — default 10MB
  maxFiles?:    number;                     // default 5
  disabled?:    boolean;
  uploadPath:   string;                     // endpoint backend: '/finance/invoices/attachments'
}

interface FileState {
  file:      File;
  progress:  number;
  error?:    string;
  uploaded?: UploadedFile;
}

export function FileUpload({
  value       = [],
  onChange,
  accept      = { 'application/pdf': ['.pdf'], 'image/*': ['.jpg', '.jpeg', '.png'] },
  maxSize     = 10 * 1024 * 1024,  // 10MB
  maxFiles    = 5,
  disabled    = false,
  uploadPath,
}: FileUploadProps) {
  const [uploading, setUploading] = useState<FileState[]>([]);

  const uploadFile = async (file: File): Promise<UploadedFile> => {
    const formData = new FormData();
    formData.append('file', file);

    const result = await api.post<UploadedFile>(uploadPath, formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
      onUploadProgress: (e) => {
        const progress = Math.round((e.loaded * 100) / (e.total ?? 1));
        setUploading((prev) =>
          prev.map((f) => f.file === file ? { ...f, progress } : f)
        );
      },
    });

    return result;
  };

  const onDrop = useCallback(async (accepted: File[]) => {
    const newStates: FileState[] = accepted.map((f) => ({
      file: f, progress: 0,
    }));
    setUploading((prev) => [...prev, ...newStates]);

    const uploaded: UploadedFile[] = [];
    for (const file of accepted) {
      try {
        const result = await uploadFile(file);
        uploaded.push(result);
        setUploading((prev) =>
          prev.map((f) =>
            f.file === file ? { ...f, progress: 100, uploaded: result } : f
          )
        );
      } catch {
        setUploading((prev) =>
          prev.map((f) =>
            f.file === file ? { ...f, error: 'Upload eșuat' } : f
          )
        );
      }
    }

    if (uploaded.length > 0) {
      onChange([...value, ...uploaded]);
      // Curăță stările finalizate după 2s
      setTimeout(() => {
        setUploading((prev) => prev.filter((f) => !f.uploaded));
      }, 2000);
    }
  }, [value, onChange, uploadPath]);

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept,
    maxSize,
    maxFiles: maxFiles - value.length,
    disabled: disabled || value.length >= maxFiles,
  });

  const removeFile = (id: string) => {
    onChange(value.filter((f) => f.id !== id));
  };

  const formatSize = (bytes: number) =>
    bytes < 1024 * 1024
      ? `${(bytes / 1024).toFixed(0)} KB`
      : `${(bytes / 1024 / 1024).toFixed(1)} MB`;

  return (
    <div className="space-y-3">
      {/* Drop zone */}
      {value.length < maxFiles && (
        <div
          {...getRootProps()}
          className={cn(
            'border-2 border-dashed rounded-lg p-6 text-center cursor-pointer transition-colors',
            isDragActive
              ? 'border-primary-400 bg-primary-50'
              : 'border-border-default hover:border-primary-300 hover:bg-surface-subtle',
            disabled && 'opacity-50 cursor-not-allowed pointer-events-none'
          )}
        >
          <input {...getInputProps()} />
          <UploadCloudIcon className="h-8 w-8 mx-auto text-text-muted mb-2" />
          <p className="text-sm text-text-secondary">
            {isDragActive
              ? 'Eliberați fișierele...'
              : 'Trageți fișierele aici sau '
            }
            {!isDragActive && (
              <span className="text-primary-500 font-medium">selectați</span>
            )}
          </p>
          <p className="text-xs text-text-muted mt-1">
            PDF, JPG, PNG — max {formatSize(maxSize)} per fișier
          </p>
        </div>
      )}

      {/* Fișiere în upload */}
      {uploading.map((state, i) => (
        <div key={i} className="flex items-center gap-3 p-3 border rounded-lg
                                border-border-default bg-surface-subtle">
          <FileIcon className="h-5 w-5 text-primary-400 shrink-0" />
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium truncate">{state.file.name}</p>
            {state.error
              ? <p className="text-xs text-danger-text">{state.error}</p>
              : <Progress value={state.progress} className="h-1 mt-1" />
            }
          </div>
          {state.uploaded && (
            <CheckCircleIcon className="h-4 w-4 text-success-icon shrink-0" />
          )}
        </div>
      ))}

      {/* Fișiere uploadate */}
      {value.map((file) => (
        <div key={file.id} className="flex items-center gap-3 p-3 border rounded-lg
                                      border-border-default">
          <FileIcon className="h-5 w-5 text-text-muted shrink-0" />
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium truncate">{file.name}</p>
            <p className="text-xs text-text-muted">{formatSize(file.size)}</p>
          </div>
          {!disabled && (
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="h-7 w-7 text-text-muted hover:text-danger-text"
              onClick={() => removeFile(file.id)}
            >
              <XIcon className="h-4 w-4" />
            </Button>
          )}
        </div>
      ))}
    </div>
  );
}

// Integrare RHF
// <FormField name="attachments" render={({ field }) => (
//   <FileUpload
//     value={field.value ?? []}
//     onChange={field.onChange}
//     uploadPath="/finance/invoices/attachments"
//   />
// )} />
```

## Reguli obligatorii
- Upload via backend — niciodată direct la MinIO din FE (securitate)
- Progress bar vizibil — feedback pentru fișiere mari
- `maxFiles` configurat — ERP nu primește fișiere nelimitat
- `accept` configurat per câmp — nu accepta orice tip
- Remove fișier = soft delete prin API (MinIO key marcat ca șters)
