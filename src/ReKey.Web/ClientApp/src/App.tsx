import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';
import Container from '@mui/material/Container';
import CssBaseline from '@mui/material/CssBaseline';
import Typography from '@mui/material/Typography';
import { ThemeProvider, createTheme } from '@mui/material/styles';

import { PasswordForm } from './components/PasswordForm';
import { useSettings } from './hooks/useSettings';

const theme = createTheme({
  typography: {
    fontFamily: '"Inter", "Roboto", "Helvetica Neue", Arial, sans-serif',
  },
  palette: {
    background: {
      default: '#f5f5f7',
    },
  },
  components: {
    MuiCard: {
      styleOverrides: {
        root: {
          borderRadius: 12,
          boxShadow: '0 4px 24px rgba(0,0,0,0.08)',
        },
      },
    },
    MuiTextField: {
      defaultProps: {
        variant: 'outlined',
        size: 'small',
      },
    },
    MuiButton: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 600,
          borderRadius: 8,
        },
      },
    },
  },
});

export default function App() {
  const { settings, loading, error } = useSettings();
  const [succeeded, setSucceeded]    = useState(false);

  // Update page title once settings are loaded
  if (settings?.applicationTitle) {
    document.title = settings.applicationTitle;
  }

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <Box
        sx={{
          minHeight: '100vh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          bgcolor: 'background.default',
          py: 4,
        }}
      >
        <Container maxWidth="sm">
          <Card>
            <CardContent sx={{ p: { xs: 3, sm: 4 } }}>

              {/* Header */}
              <Typography variant="h5" fontWeight={600} gutterBottom>
                {settings?.changePasswordTitle ?? 'Change Account Password'}
              </Typography>

              {/* Loading */}
              {loading && (
                <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                  <CircularProgress />
                </Box>
              )}

              {/* Settings load error */}
              {!loading && error && (
                <Alert severity="error">
                  Unable to load application settings. Please refresh the page or contact IT Support.
                </Alert>
              )}

              {/* Success state */}
              {!loading && !error && succeeded && (
                <Box>
                  <Alert severity="success" sx={{ mb: 2 }}>
                    <Typography fontWeight={600}>
                      {settings?.alerts?.successAlertTitle ?? 'Password changed successfully.'}
                    </Typography>
                    {settings?.alerts?.successAlertBody && (
                      <Typography variant="body2" sx={{ mt: 0.5 }}>
                        {settings.alerts.successAlertBody}
                      </Typography>
                    )}
                  </Alert>
                  <Button
                    variant="outlined"
                    onClick={() => setSucceeded(false)}
                    fullWidth
                  >
                    Change another password
                  </Button>
                </Box>
              )}

              {/* Form */}
              {!loading && !error && !succeeded && settings && (
                <PasswordForm settings={settings} onSuccess={() => setSucceeded(true)} />
              )}

            </CardContent>
          </Card>
        </Container>
      </Box>
    </ThemeProvider>
  );
}
