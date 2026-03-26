import { useEffect, useRef, useState } from 'react';

declare global {
  interface Window {
    grecaptcha?: {
      ready: (cb: () => void) => void;
      execute: (siteKey: string, options: { action: string }) => Promise<string>;
    };
  }
}

interface UseRecaptchaResult {
  ready: boolean;
  executeRecaptcha: () => Promise<string>;
}

export function useRecaptcha(siteKey: string | undefined): UseRecaptchaResult {
  const [ready, setReady] = useState(false);
  const scriptRef         = useRef<HTMLScriptElement | null>(null);

  useEffect(() => {
    if (!siteKey) return;

    const existing = document.getElementById('recaptcha-script');
    if (!existing) {
      const script  = document.createElement('script');
      script.id     = 'recaptcha-script';
      script.src    = `https://www.google.com/recaptcha/api.js?render=${siteKey}`;
      script.async  = true;
      script.defer  = true;
      document.head.appendChild(script);
      scriptRef.current = script;
    }

    const interval = setInterval(() => {
      if (window.grecaptcha) {
        window.grecaptcha.ready(() => setReady(true));
        clearInterval(interval);
      }
    }, 100);

    return () => clearInterval(interval);
  }, [siteKey]);

  const executeRecaptcha = async (): Promise<string> => {
    if (!siteKey || !window.grecaptcha || !ready) return '';
    return window.grecaptcha.execute(siteKey, { action: 'change_password' });
  };

  return { ready, executeRecaptcha };
}
