// Re-mounts on every navigation, so each page gets the fade and slide-up.
export default function AppTemplate({ children }) {
  return <div className="glass-page-enter flex flex-col gap-6">{children}</div>;
}
