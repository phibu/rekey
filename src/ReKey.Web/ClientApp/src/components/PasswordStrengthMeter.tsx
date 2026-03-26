import { useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';

interface Props {
  password: string;
}

const LABELS = ['Very Weak', 'Weak', 'Fair', 'Strong', 'Very Strong'] as const;
const COLORS = ['error', 'error', 'warning', 'success', 'success'] as const;
const VALUES = [20, 40, 60, 80, 100] as const;

type ZxcvbnFn = (password: string) => { score: 0 | 1 | 2 | 3 | 4 };

export function PasswordStrengthMeter({ password }: Props) {
  const [score, setScore] = useState<0 | 1 | 2 | 3 | 4>(0);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!password) return;
    let cancelled = false;
    import('zxcvbn').then(mod => {
      if (!cancelled) {
        setLoaded(true);
        const zxcvbn = mod.default as ZxcvbnFn;
        setScore(zxcvbn(password).score);
      }
    }).catch(() => {/* ignore load errors */});
    return () => { cancelled = true; };
  }, [password]);

  if (!password || !loaded) return null;

  return (
    <Box sx={{ mt: 0.5 }}>
      <LinearProgress
        variant="determinate"
        value={VALUES[score]}
        color={COLORS[score]}
        sx={{ height: 6, borderRadius: 3 }}
      />
      <Typography variant="caption" color={`${COLORS[score]}.main`} sx={{ mt: 0.25, display: 'block' }}>
        {LABELS[score]}
      </Typography>
    </Box>
  );
}
