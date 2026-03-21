import { render } from 'preact';
import './style.css';
import { useEffect, useState } from 'preact/hooks';

function App() {
	const [backendMessage, setBackendMessage] = useState("Waiting for C#...");

	useEffect(() => {
		fetch('https://localhost:7140/api/ping')
			.then(response => response.text())
			.then(data => setBackendMessage(data))
			.catch(error => setBackendMessage("C# Backend is offline! " + error));
	}, []);

	return (
		<div>
			<h1>THE Review App</h1>
			<p>{backendMessage}</p>
		</div>
	);
}

render(<App />, document.getElementById('app'));
