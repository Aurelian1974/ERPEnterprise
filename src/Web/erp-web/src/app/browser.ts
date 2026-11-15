// Navigarea in afara aplicatiei React (paginile Razor /account/*), izolata pentru teste.
export const browser = {
  currentPath(): string {
    return window.location.pathname + window.location.search;
  },
  navigate(url: string): void {
    window.location.assign(url);
  },
};
