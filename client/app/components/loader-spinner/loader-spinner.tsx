const LogoLoadingSpinner = ({ size = 64 }: { size?: number }) => (
  <>
    <style>{`
      @keyframes breathe {
        0%, 100% { transform: scaleY(1); }
        50% { transform: scaleY(0.85); }
      }
      .animate-breathe {
        animation: breathe 2s ease-in-out infinite;
        transform-origin: center;
      }
    `}</style>
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background">
      <img src="/Icon.svg" alt="Loading" className="animate-breathe" style={{ width: size, height: size }} />
    </div>
  </>
);

export default LogoLoadingSpinner;
