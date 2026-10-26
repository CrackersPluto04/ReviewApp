import { render } from 'preact';
import { LoginPage } from './pages/LoginPage';
import { HomePage } from './pages/HomePage';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { PageContainer } from './components/PageContainer';
import { useMemo, useState } from 'preact/hooks';
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

function App() {
	const [mode, setMode] = useState<"light" | "dark">(() => {
		return (localStorage.getItem("theme") as ("light" | "dark")) || "light";
	});

	const theme = useMemo(() => createTheme({
		palette: {
			mode: mode,
		},
	}), [mode]);

	const toggleTheme = () => {
		const newMode = mode === "light" ? "dark" : "light";
		setMode(newMode);
		localStorage.setItem("theme", newMode);
	};

	return <ThemeProvider theme={theme}>
		<CssBaseline />

		<AuthProvider>
			<NotificationProvider>
				<AchievementToastProvider>
					<BrowserRouter>
						<PremiumUpsellProvider>
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

								{/* Where the payment provider sends the user back after checkout */}
								<Route path="/subscription/return" element={
									<PageContainer mode={mode} toggleTheme={toggleTheme}>
										<SubscriptionReturnPage />
									</PageContainer>
								} />

								<Route path="*" element={<Navigate to="/home" replace />} />
							</Routes>
						</PremiumUpsellProvider>
					</BrowserRouter>
				</AchievementToastProvider>
			</NotificationProvider>
		</AuthProvider>
	</ThemeProvider>
}

render(<App />, document.getElementById('app')!);
