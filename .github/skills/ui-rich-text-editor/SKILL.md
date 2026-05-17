---
name: ui-rich-text-editor
description: >-
  Componentă RichTextEditor pentru ERP bazată pe TipTap. Toolbar configurabil,
  integrat cu React Hook Form via Controller, output HTML sau JSON,
  extensions: Bold, Italic, Lists, Tables, Link, Placeholder.
---

# RichTextEditor — TipTap Component

## Când se aplică
Când utilizatorul cere un câmp de text formatat (note, descrieri, comentarii,
conținut document) integrat cu React Hook Form într-un formular ERP.

## Instalare
```bash
npm install @tiptap/react @tiptap/pm
npm install @tiptap/starter-kit @tiptap/extension-placeholder
npm install @tiptap/extension-link @tiptap/extension-table
npm install @tiptap/extension-table-row @tiptap/extension-table-header
npm install @tiptap/extension-table-cell @tiptap/extension-character-count
```

---

## 1. Componentă principală

```tsx
// components/common/RichTextEditor/RichTextEditor.tsx
import { useEditor, EditorContent } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import Placeholder from '@tiptap/extension-placeholder';
import Link from '@tiptap/extension-link';
import CharacterCount from '@tiptap/extension-character-count';
import { RichTextToolbar } from './RichTextToolbar';
import { cn } from '@/lib/utils';

export type ToolbarVariant = 'minimal' | 'standard' | 'full';

interface RichTextEditorProps {
  value?:         string;           // HTML string
  onChange?:      (html: string) => void;
  placeholder?:   string;
  disabled?:      boolean;
  toolbar?:       ToolbarVariant;
  maxLength?:     number;
  minHeight?:     string;
  className?:     string;
}

export function RichTextEditor({
  value       = '',
  onChange,
  placeholder = 'Introduceți textul...',
  disabled    = false,
  toolbar     = 'standard',
  maxLength,
  minHeight   = '120px',
  className,
}: RichTextEditorProps) {
  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3] },
        codeBlock: false,
      }),
      Placeholder.configure({ placeholder }),
      Link.configure({
        openOnClick: false,
        HTMLAttributes: { class: 'text-primary-500 underline' },
      }),
      ...(maxLength
        ? [CharacterCount.configure({ limit: maxLength })]
        : []),
    ],
    content:  value,
    editable: !disabled,
    onUpdate: ({ editor }) => {
      // Returnează string gol în loc de '<p></p>' când e gol
      const html = editor.isEmpty ? '' : editor.getHTML();
      onChange?.(html);
    },
  });

  const charCount    = editor?.storage.characterCount?.characters?.() ?? 0;
  const isOverLimit  = maxLength ? charCount > maxLength : false;

  return (
    <div
      className={cn(
        'rounded-md border border-border-default bg-white',
        'focus-within:border-primary-500 focus-within:ring-1 focus-within:ring-primary-500',
        disabled && 'opacity-60 cursor-not-allowed bg-surface-subtle',
        isOverLimit && 'border-danger-icon focus-within:border-danger-icon',
        className
      )}
    >
      {editor && toolbar !== 'minimal' && (
        <RichTextToolbar editor={editor} variant={toolbar} />
      )}

      <EditorContent
        editor={editor}
        style={{ minHeight }}
        className={cn(
          'prose prose-sm max-w-none px-3 py-2',
          'focus:outline-none',
          '[&_.ProseMirror]:outline-none',
          '[&_.ProseMirror_p.is-editor-empty:first-child::before]:content-[attr(data-placeholder)]',
          '[&_.ProseMirror_p.is-editor-empty:first-child::before]:text-muted-foreground',
          '[&_.ProseMirror_p.is-editor-empty:first-child::before]:pointer-events-none',
          '[&_.ProseMirror_p.is-editor-empty:first-child::before]:float-left',
          '[&_.ProseMirror_p.is-editor-empty:first-child::before]:h-0',
        )}
      />

      {maxLength && (
        <div className={cn(
          'px-3 py-1 text-xs border-t border-border-subtle text-right',
          isOverLimit ? 'text-danger-text' : 'text-text-muted'
        )}>
          {charCount} / {maxLength}
        </div>
      )}
    </div>
  );
}
```

---

## 2. Toolbar

```tsx
// components/common/RichTextEditor/RichTextToolbar.tsx
import type { Editor } from '@tiptap/react';
import {
  Bold, Italic, Strikethrough, List, ListOrdered,
  Heading2, Heading3, Link2, Undo, Redo, Minus,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Separator } from '@/components/ui/separator';
import { cn } from '@/lib/utils';
import type { ToolbarVariant } from './RichTextEditor';

interface ToolbarButton {
  icon:     React.ReactNode;
  label:    string;
  action:   () => void;
  isActive: boolean;
  show:     ToolbarVariant[];
}

interface RichTextToolbarProps {
  editor:  Editor;
  variant: ToolbarVariant;
}

export function RichTextToolbar({ editor, variant }: RichTextToolbarProps) {
  const buttons: ToolbarButton[] = [
    {
      icon:     <Bold className="h-3.5 w-3.5" />,
      label:    'Bold',
      action:   () => editor.chain().focus().toggleBold().run(),
      isActive: editor.isActive('bold'),
      show:     ['minimal', 'standard', 'full'],
    },
    {
      icon:     <Italic className="h-3.5 w-3.5" />,
      label:    'Italic',
      action:   () => editor.chain().focus().toggleItalic().run(),
      isActive: editor.isActive('italic'),
      show:     ['minimal', 'standard', 'full'],
    },
    {
      icon:     <Strikethrough className="h-3.5 w-3.5" />,
      label:    'Tăiat',
      action:   () => editor.chain().focus().toggleStrike().run(),
      isActive: editor.isActive('strike'),
      show:     ['standard', 'full'],
    },
    {
      icon:     <Heading2 className="h-3.5 w-3.5" />,
      label:    'Titlu 2',
      action:   () => editor.chain().focus().toggleHeading({ level: 2 }).run(),
      isActive: editor.isActive('heading', { level: 2 }),
      show:     ['standard', 'full'],
    },
    {
      icon:     <Heading3 className="h-3.5 w-3.5" />,
      label:    'Titlu 3',
      action:   () => editor.chain().focus().toggleHeading({ level: 3 }).run(),
      isActive: editor.isActive('heading', { level: 3 }),
      show:     ['full'],
    },
    {
      icon:     <List className="h-3.5 w-3.5" />,
      label:    'Listă',
      action:   () => editor.chain().focus().toggleBulletList().run(),
      isActive: editor.isActive('bulletList'),
      show:     ['standard', 'full'],
    },
    {
      icon:     <ListOrdered className="h-3.5 w-3.5" />,
      label:    'Listă numerotată',
      action:   () => editor.chain().focus().toggleOrderedList().run(),
      isActive: editor.isActive('orderedList'),
      show:     ['standard', 'full'],
    },
    {
      icon:     <Minus className="h-3.5 w-3.5" />,
      label:    'Separator',
      action:   () => editor.chain().focus().setHorizontalRule().run(),
      isActive: false,
      show:     ['full'],
    },
    {
      icon:     <Undo className="h-3.5 w-3.5" />,
      label:    'Anulează',
      action:   () => editor.chain().focus().undo().run(),
      isActive: false,
      show:     ['standard', 'full'],
    },
    {
      icon:     <Redo className="h-3.5 w-3.5" />,
      label:    'Refă',
      action:   () => editor.chain().focus().redo().run(),
      isActive: false,
      show:     ['standard', 'full'],
    },
  ];

  const visible = buttons.filter((b) => b.show.includes(variant));

  return (
    <div className="flex flex-wrap items-center gap-0.5 border-b border-border-subtle px-2 py-1">
      {visible.map((btn, i) => (
        <Button
          key={i}
          type="button"
          variant="ghost"
          size="icon"
          className={cn(
            'h-7 w-7',
            btn.isActive && 'bg-primary-100 text-primary-700'
          )}
          onClick={btn.action}
          title={btn.label}
        >
          {btn.icon}
        </Button>
      ))}
    </div>
  );
}
```

---

## 3. Integrare React Hook Form

```tsx
// Utilizare în formular
import { Controller } from 'react-hook-form';
import { RichTextEditor } from '@/components/common/RichTextEditor/RichTextEditor';
import { FormField, FormItem, FormLabel, FormMessage } from '@/components/ui/form';

// Schema Zod
const schema = z.object({
  notes:       z.string().optional(),
  description: z.string().min(10, 'Minim 10 caractere').max(5000),
});

// Toolbar minimal — doar Bold, Italic
<FormField
  control={form.control}
  name="notes"
  render={({ field }) => (
    <FormItem>
      <FormLabel>Note interne</FormLabel>
      <RichTextEditor
        value={field.value ?? ''}
        onChange={field.onChange}
        toolbar="minimal"
        placeholder="Adaugă note..."
        minHeight="80px"
        maxLength={1000}
      />
      <FormMessage />
    </FormItem>
  )}
/>

// Toolbar standard — ERP standard
<FormField
  control={form.control}
  name="description"
  render={({ field }) => (
    <FormItem>
      <FormLabel>Descriere</FormLabel>
      <RichTextEditor
        value={field.value ?? ''}
        onChange={field.onChange}
        toolbar="standard"
        minHeight="200px"
      />
      <FormMessage />
    </FormItem>
  )}
/>
```

---

## 4. Variantele toolbar

| Variant | Butoane active | Utilizare |
|---|---|---|
| `minimal` | Bold, Italic | Note scurte, comentarii |
| `standard` | Bold, Italic, Strike, H2, Lists, Undo/Redo | Descrieri, specificații |
| `full` | Toate | Conținut document, contracte |

## Reguli obligatorii
- Output = HTML string — stocat în DB ca `NVARCHAR(MAX)`
- Editor gol → string gol `''` — niciodată `'<p></p>'`
- `maxLength` afișat cu contor vizibil
- `disabled` = editor non-editable, nu ascuns
- Toolbar `type="button"` pe fiecare buton — evită submit accidental al formularului
- Validare Zod pe string HTML — `z.string().min()` funcționează pe conținut raw
