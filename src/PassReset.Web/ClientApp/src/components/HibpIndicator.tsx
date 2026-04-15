import Alert from '@mui/material/Alert';
import LinearProgress from '@mui/material/LinearProgress';
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined';
import GppBadOutlinedIcon from '@mui/icons-material/GppBadOutlined';
import type { HibpState } from '../hooks/useHibpCheck';

interface Props {
  state: HibpState;
  count: number;
  failOpen: boolean;
}

/**
 * FEAT-004: inline breach indicator rendered directly below the new-password
 * field. Styling and copy follow 03-UI-SPEC §Component Inventory #3.
 * Fail-open policy: when HIBP is unreachable and failOpen=true we render nothing
 * (matches server behavior); when failOpen=false we show a neutral warning.
 */
export default function HibpIndicator({ state, count, failOpen }: Props) {
  switch (state) {
    case 'idle':
      return null;

    case 'checking':
      return (
        <LinearProgress
          aria-label="Checking password breach status"
          sx={{ mt: 0.5, mb: 1, height: 2 }}
        />
      );

    case 'safe':
      return (
        <Alert
          severity="success"
          icon={<ShieldOutlinedIcon fontSize="small" />}
          role="status"
          sx={{ mt: 0.5, mb: 1, py: 0.5 }}
        >
          No known breaches found.
        </Alert>
      );

    case 'breached':
      return (
        <Alert
          severity="error"
          icon={<GppBadOutlinedIcon fontSize="small" />}
          role="alert"
          sx={{ mt: 0.5, mb: 1, py: 0.5 }}
        >
          Found in {count.toLocaleString()} data breach{count === 1 ? '' : 'es'}. Choose a
          different password.
        </Alert>
      );

    case 'unavailable':
      if (failOpen) return null;
      return (
        <Alert severity="warning" role="status" sx={{ mt: 0.5, mb: 1, py: 0.5 }}>
          Could not verify password safety. Proceed with caution.
        </Alert>
      );

    default:
      return null;
  }
}
