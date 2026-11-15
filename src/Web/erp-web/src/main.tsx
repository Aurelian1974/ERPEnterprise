import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router';
import { createRouter } from './app/routes';
import '@syncfusion/ej2-tailwind3-theme/styles/base/base.css';
import '@syncfusion/ej2-tailwind3-theme/styles/button/button.css';
import '@syncfusion/ej2-tailwind3-theme/styles/input/input.css';
import '@syncfusion/ej2-tailwind3-theme/styles/popup/popup.css';
import '@syncfusion/ej2-tailwind3-theme/styles/list-view/list-view.css';
import '@syncfusion/ej2-tailwind3-theme/styles/drop-down-base/drop-down-base.css';
import '@syncfusion/ej2-tailwind3-theme/styles/drop-down-list/drop-down-list.css';
import '@syncfusion/ej2-tailwind3-theme/styles/drop-down-button/drop-down-button.css';
import '@syncfusion/ej2-tailwind3-theme/styles/textbox/textbox.css';
import '@syncfusion/ej2-tailwind3-theme/styles/numerictextbox/numerictextbox.css';
import '@syncfusion/ej2-tailwind3-theme/styles/switch/switch.css';
import '@syncfusion/ej2-tailwind3-theme/styles/check-box/check-box.css';
import '@syncfusion/ej2-tailwind3-theme/styles/treeview/treeview.css';
import '@syncfusion/ej2-tailwind3-theme/styles/grid/grid.css';
import '@syncfusion/ej2-tailwind3-theme/styles/pager/pager.css';
import './styles.css';

const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false } },
});

const root = document.getElementById('root');
if (root === null) {
  throw new Error('Elementul #root lipsește din index.html.');
}

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={createRouter()} />
    </QueryClientProvider>
  </StrictMode>,
);
