import { LoginPage } from './pages/LoginPage';
import { HomePage } from './pages/HomePage';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { PageContainer } from './components/PageContainer';
import { useMemo, useState } from 'preact/hooks';
import { ReactNode } from 'preact/compat';
import { createTheme, CssBaseline, ThemeProvider } from '@mui/material';
import { SearchPage } from './pages/SearchPage';
import { MediaReviewPage } from './pages/MediaReviewPage';
import { ScrollToTop } from './components/ScrollToTop';
import { AuthProvider } from './context/AuthContext';
import { DiscoverPage } from './pages/DiscoverPage';
import { DiscoverMusicPage } from './pages/DiscoverMusicPage';
import { ProfileLayout } from './pages/ProfileLayout';
import { CollectionsTab } from './components/CollectionsTab';
import { ReviewsTab } from './components/ReviewsTab';
import { AchievementsTab } from './components/AchievementsTab';
import { AchievementToastProvider } from './context/AchievementToastContext';
import { NotificationProvider } from './context/NotificationContext';
import { ReviewPage } from './pages/ReviewPage';
import { EmailLinkPage } from './pages/EmailLinkPage';
import { PremiumPage } from './pages/PremiumPage';
import { SubscriptionReturnPage } from './pages/SubscriptionReturnPage';
import { PremiumUpsellProvider } from './context/PremiumUpsellContext';
import { RecognitionPage } from './pages/RecognitionPage';

export type ThemeMode = "light" | "dark";

type ThemeProps = {
	mode: ThemeMode;
	toggleTheme: () => void;
};

/**
 * The light / dark mode, persisted to `localStorage` under `theme`; light on the first visit.
 * @returns The current mode and a function that switches it.
 */
export function useThemeMode(): ThemeProps {
	const [mode, setMode] = useState<ThemeMode>(() => {
		return (localStorage.getItem("theme") as ThemeMode) || "light";
	});

	const toggleTheme = () => {
		const newMode = mode === "light" ? "dark" : "light";
		setMode(newMode);
		localStorage.setItem("theme", newMode);
	};

	return { mode, toggleTheme };
}

type AppProvidersProps = {
	mode: ThemeMode;
	children: ReactNode;
};

/**
 * The global providers that need no router: the MUI theme, then auth, the notification count and the
 * achievement toasts. The order matters: the notification provider reads the auth state, so it sits inside
 * `AuthProvider`. Exported separately from `App` so tests can mount the app under their own router.
 */
export function AppProviders({ mode, children }: AppProvidersProps) {
	const theme = useMemo(() => createTheme({
		palette: {
			mode: mode,
		},
	}), [mode]);

	return <ThemeProvider theme={theme}>
		<CssBaseline />

		<AuthProvider>
			<NotificationProvider>
				<AchievementToastProvider>
					{children}
				</AchievementToastProvider>
			</NotificationProvider>
		</AuthProvider>
	</ThemeProvider>
}

/**
 * The route table, with the providers that need the router (the Premium upsell dialog navigates to the plans
 * page). Most pages are wrapped in `PageContainer` for the shared header and footer; the profile is a layout
 * route whose tabs render in `ProfileLayout`'s `Outlet`, and the overview tab is still a placeholder. Unknown
 * paths go to `/home`.
 */
export function AppRoutes({ mode, toggleTheme }: ThemeProps) {
	return <PremiumUpsellProvider>
		<ScrollToTop />

		<Routes>
			<Route path="/login" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<LoginPage />
				</PageContainer>
			} />

			<Route path="/home" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<HomePage />
				</PageContainer>
			} />

			<Route path="/search" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<SearchPage />
				</PageContainer>
			} />

			<Route path="/discover" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<DiscoverPage />
				</PageContainer>
			} />

			<Route path="/discover/music" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<DiscoverMusicPage />
				</PageContainer>
			} />

			<Route path="/media/:mediaType/:externalApiId" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<MediaReviewPage />
				</PageContainer>
			} />

			<Route path="/review/:reviewId" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<ReviewPage />
				</PageContainer>
			} />

			{/* Landing pages of the links in account emails */}
			<Route path="/verify-email" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<EmailLinkPage mode="verify" />
				</PageContainer>
			} />

			<Route path="/confirm-email-change" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<EmailLinkPage mode="change" />
				</PageContainer>
			} />

			<Route path="/profile/:username" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<ProfileLayout />
				</PageContainer>
			}>
				{/* Nested routes for the profile layout */}
				<Route index element={<Navigate to="overview" replace />} />

				<Route path="overview" element={<div>Overview Tab (Coming Soon)</div>} />
				<Route path="reviews" element={<ReviewsTab />} />
				<Route path="collections" element={<CollectionsTab />} />
				<Route path="achievements" element={<AchievementsTab />} />
			</Route>

			<Route path="/premium" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<PremiumPage />
				</PageContainer>
			} />

			{/* Camera recognition (Premium; the page shows the upsell to Free users) */}
			<Route path="/recognize" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<RecognitionPage />
				</PageContainer>
			} />

			{/* Where the payment provider sends the user back after checkout */}
			<Route path="/subscription/return" element={
				<PageContainer mode={mode} toggleTheme={toggleTheme}>
					<SubscriptionReturnPage />
				</PageContainer>
			} />

			<Route path="*" element={<Navigate to="/home" replace />} />
		</Routes>
	</PremiumUpsellProvider>
}

/** The application root: the theme state, the global providers and the browser router with the routes. */
export function App() {
	const { mode, toggleTheme } = useThemeMode();

	return <AppProviders mode={mode}>
		<BrowserRouter>
			<AppRoutes mode={mode} toggleTheme={toggleTheme} />
		</BrowserRouter>
	</AppProviders>
}
